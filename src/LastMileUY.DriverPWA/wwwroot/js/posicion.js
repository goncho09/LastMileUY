window.lastMilePosicion = {
    iniciar(dotnet, intervaloMs) {
        if (!navigator.geolocation) return -1;

        let ultima = 0;

        return navigator.geolocation.watchPosition(
            p => {
                const ahora = Date.now();
                if (ahora - ultima < intervaloMs) return;
                ultima = ahora;
                dotnet.invokeMethodAsync(
                    "PosicionRecibida",
                    Number(p.coords.latitude.toFixed(6)),
                    Number(p.coords.longitude.toFixed(6)),
                    new Date(p.timestamp).toISOString());
            },
            () => { },
            { enableHighAccuracy: true, maximumAge: intervaloMs, timeout: 30000 });
    },

    detener(id) {
        if (id >= 0 && navigator.geolocation) navigator.geolocation.clearWatch(id);
    }
};
