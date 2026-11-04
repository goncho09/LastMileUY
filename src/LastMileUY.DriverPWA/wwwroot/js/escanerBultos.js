
// La cámara es una sola para toda la hoja de ruta, aunque cada parada tenga su botón de escanear.
// Por eso el escáner recuerda qué parada la está usando (su "dueño"): si otra parada la abre,
// se le avisa a la anterior para que deje de mostrar la cámara, y una parada que se cierra
// solo apaga la cámara si es suya.
window.lastMileEscaner = (() => {
    let lector = null;
    let duenoId = null;
    let duenoRef = null;
    let inicio = null;
    let pedido = null;
    let detectado = false;

    // Apaga la cámara, sea de quien sea. Si todavía está arrancando, espera a que termine para
    // poder apagarla (si no, quedaría prendida).
    async function apagar() {
        const actual = lector;
        const arrancando = inicio;

        lector = null;
        duenoId = null;
        duenoRef = null;
        inicio = null;

        if (!actual) return;

        if (arrancando) {
            try {
                await arrancando;
            } catch {
                // Si no llegó a arrancar, no hay nada que apagar.
            }
        }

        try {
            await actual.stop();
        } catch {
            // Puede no haber llegado a arrancar.
        }

        try {
            actual.clear();
        } catch {
            // Puede estar desmontado.
        }
    }

    // Con elementoId, apaga la cámara solo si la tiene (o la está pidiendo) esa parada.
    async function detener(elementoId) {
        if (elementoId && elementoId !== duenoId && elementoId !== pedido?.id) return;

        pedido = null;
        await apagar();
    }

    async function iniciar(elementoId, referenciaDotNet) {
        // Se anota el pedido antes de esperar nada: si mientras tanto se cancela o lo pide
        // otra parada, este ya no sigue.
        const anteriores = [
            duenoId !== elementoId ? duenoRef : null,
            pedido && pedido.id !== elementoId ? pedido.ref : null
        ];
        const mio = { id: elementoId, ref: referenciaDotNet };
        pedido = mio;

        await apagar();

        for (const anterior of new Set(anteriores)) {
            anterior?.invokeMethodAsync("EscanerCerrado").catch(() => {});
        }

        if (pedido !== mio) return;
        pedido = null;

        if (!window.Html5Qrcode) {
            throw new Error("No se pudo cargar el lector de códigos.");
        }

        if (!navigator.mediaDevices?.getUserMedia) {
            throw new Error("La cámara requiere HTTPS o localhost.");
        }

        const actual = new Html5Qrcode(elementoId, {
            formatsToSupport: [
                Html5QrcodeSupportedFormats.QR_CODE,
                Html5QrcodeSupportedFormats.CODE_128,
                Html5QrcodeSupportedFormats.CODE_39,
                Html5QrcodeSupportedFormats.EAN_13
            ],
            verbose: false
        });

        lector = actual;
        duenoId = elementoId;
        duenoRef = referenciaDotNet;
        detectado = false;

        inicio = actual.start(
            { facingMode: "environment" },
            {
                fps: 10,
                qrbox: (ancho, alto) => {
                    const lado = Math.min(250, ancho * 0.8, alto * 0.8);
                    return { width: Math.floor(lado), height: Math.floor(lado) };
                }
            },
            async (codigo) => {
                if (lector !== actual || detectado) return;

                detectado = true;
                await apagar();

                try {
                    await referenciaDotNet.invokeMethodAsync("CodigoEscaneado", codigo);
                } catch {
                    // La parada pudo haberse cerrado mientras tanto.
                }
            },
            () => {
                // No detectar un código en un fotograma es normal.
            }
        );

        try {
            await inicio;

            if (lector === actual) inicio = null;
        } catch (e) {
            // Si otra parada ya tomó la cámara, ese aviso es el que vale.
            if (lector !== actual) return;

            await apagar();
            throw e;
        }
    }

    return { iniciar, detener };
})();
