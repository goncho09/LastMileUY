
window.lastMileEscaner = (() => {
    let lector = null;
    let activo = false;
    let iniciando = false;
    let detectado = false;

    async function detener() {
        activo = false;
        detectado = true;

        const actual = lector;
        lector = null;

        if (!actual) return;

        try {
            if (iniciando) {
                // El inicio todavía debe finalizar antes de detenerse.
                return;
            }

            await actual.stop();
        } catch (e) {
        }

        try {
            actual.clear();
        } catch {
            // Puede estar desmontado.
        }
    }

    async function iniciar(elementoId, referenciaDotNet) {
        await detener();

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
        detectado = false;
        iniciando = true;

        try {
            await actual.start(
                { facingMode: "environment" },
                {
                    fps: 10,
                    qrbox: (ancho, alto) => {
                        const lado = Math.min(250, ancho * 0.8, alto * 0.8);
                        return { width: Math.floor(lado), height: Math.floor(lado) };
                    }
                },
                async (codigo) => {
                    if (!activo || detectado) return;

                    detectado = true;
                    await detener();

                    try {
                        await referenciaDotNet.invokeMethodAsync(
                            "CodigoEscaneado",
                            codigo
                        );
                    } catch (e) {
                    }
                },
                () => {
                    // No detectar un código en un fotograma es normal.
                }
            );

            iniciando = false;

            if (lector !== actual) {
                await actual.stop().catch(() => {});
                actual.clear();
                return;
            }

            activo = true;
        } catch (e) {
            iniciando = false;

            try {
                await detener();
            } catch {
                // Ignorar errores al detener el escáner.
            }

            throw e;
        }
    }

    return { iniciar, detener };
})();
