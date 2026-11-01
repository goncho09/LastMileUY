
(() => {
    "use strict";

    if (!window.signalR || window.LastMileUYSignalR) {
        return;
    }

    const conexion = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/envios")
        .withAutomaticReconnect()
        .build();

    let activo = true;
    let temporizador = null;

    function notificar(nombre, detalle = {}) {
        window.dispatchEvent(new CustomEvent(nombre, { detail: detalle }));
    }

    conexion.on("EnvioActualizado", evento => {
        notificar("lastmileuy:envio-actualizado", evento);
    });

    conexion.onreconnecting(() => {
        notificar("lastmileuy:signalr-estado", { estado: "Reconectando..." });
    });

    conexion.onreconnected(() => {
        notificar("lastmileuy:signalr-estado", { estado: "En vivo" });
        notificar("lastmileuy:signalr-sincronizar");
    });

    conexion.onclose(() => {
        notificar("lastmileuy:signalr-estado", { estado: "Desconectado" });

        if (activo) {
            programarConexion();
        }
    });

    function programarConexion() {
        if (!activo || temporizador !== null) return;

        temporizador = setTimeout(() => {
            temporizador = null;
            void conectar();
        }, 5000);
    }

    async function conectar() {
        if (!activo ||
            conexion.state !== signalR.HubConnectionState.Disconnected) {
            return;
        }

        try {
            notificar("lastmileuy:signalr-estado", { estado: "Conectando..." });

            await conexion.start();

            notificar("lastmileuy:signalr-estado", { estado: "En vivo" });
            notificar("lastmileuy:signalr-sincronizar");
        } catch (error) {
            console.error("Error conectando SignalR:", error);
            notificar("lastmileuy:signalr-estado", { estado: "Sin conexión" });
            programarConexion();
        }
    }

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) {
            notificar("lastmileuy:signalr-sincronizar");
        }
    });

    window.addEventListener("pagehide", () => {
        activo = false;
        clearTimeout(temporizador);
        void conexion.stop();
    });

    window.LastMileUYSignalR = { conexion };

    void conectar();
})();
