(() => {
    const contenedor = document.getElementById("contenido-flota");
    const elementoMapa = document.getElementById("mapa-flota");
    if (!contenedor || !elementoMapa || !window.L) return;

    const mapa = L.map(elementoMapa).setView([-34.88, -56.17], 12);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 18,
        attribution: "&copy; OpenStreetMap"
    }).addTo(mapa);

    const capa = L.layerGroup().addTo(mapa);
    let primeraVez = true;

    function texto(valor) {
        const div = document.createElement("div");
        div.textContent = valor;
        return div.innerHTML;
    }

    function dibujar() {
        let marcadores = [];
        try {
            marcadores = JSON.parse(contenedor.dataset.marcadores || "[]");
        } catch {
            marcadores = [];
        }

        capa.clearLayers();
        const puntos = [];

        for (const m of marcadores) {
            const punto = [Number(m.latitud), Number(m.longitud)];
            puntos.push(punto);
            L.marker(punto)
                .bindPopup(`<strong>${texto(m.ruta)}</strong><br>${texto(m.repartidor)} · ${texto(m.vehiculo)}<br>${texto(m.hace)}`)
                .addTo(capa);
        }

        if (primeraVez && puntos.length > 0) {
            mapa.fitBounds(puntos, { padding: [40, 40], maxZoom: 14 });
            primeraVez = false;
        }
    }

    let actualizando = false;

    async function actualizar() {
        if (actualizando) return;
        actualizando = true;

        try {
            const respuesta = await fetch(window.location.href, { credentials: "same-origin", cache: "no-store" });
            if (!respuesta.ok) return;

            const documento = new DOMParser().parseFromString(await respuesta.text(), "text/html");
            const nuevo = documento.getElementById("contenido-flota");
            if (!nuevo) return;

            contenedor.innerHTML = nuevo.innerHTML;
            contenedor.dataset.marcadores = nuevo.dataset.marcadores || "[]";
            dibujar();
        } catch (error) {
            console.error("Error actualizando la flota:", error);
        } finally {
            actualizando = false;
        }
    }

    dibujar();

    const temporizador = setInterval(actualizar, 20000);
    window.addEventListener("lastmileuy:envio-actualizado", actualizar);
    window.addEventListener("lastmileuy:signalr-sincronizar", actualizar);
    window.addEventListener("pagehide", () => clearInterval(temporizador));
})();
