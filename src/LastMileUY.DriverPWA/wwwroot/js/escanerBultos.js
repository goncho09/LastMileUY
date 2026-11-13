window.lastMileEscaner = (() => {
    let lector = null;
    let duenoId = null;
    let duenoRef = null;
    let inicio = null;
    let pedido = null;
    let detectado = false;

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
            }
        }

        try {
            await actual.stop();
        } catch {
        }

        try {
            actual.clear();
        } catch {
        }
    }

    async function detener(elementoId) {
        if (elementoId && elementoId !== duenoId && elementoId !== pedido?.id) return;

        pedido = null;
        await apagar();
    }

    async function iniciar(elementoId, referenciaDotNet) {
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
                }
            },
            () => {
            }
        );

        try {
            await inicio;

            if (lector === actual) inicio = null;
        } catch (e) {
            if (lector !== actual) return;

            await apagar();
            throw e;
        }
    }

    return { iniciar, detener };
})();
