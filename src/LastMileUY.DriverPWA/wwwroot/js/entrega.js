window.lastMileEntrega = {
    ubicacion() {
        return new Promise(resolve => {
            if (!navigator.geolocation) {
                resolve(null);
                return;
            }

            navigator.geolocation.getCurrentPosition(
                p => resolve({
                    latitud: Number(p.coords.latitude.toFixed(6)),
                    longitud: Number(p.coords.longitude.toFixed(6))
                }),
                () => resolve(null),
                { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
        });
    },

    iniciarFirma(canvas) {
        const contexto = canvas.getContext("2d");
        contexto.lineWidth = 3;
        contexto.lineCap = "round";
        contexto.strokeStyle = "#1e1b4b";
        canvas.dataset.vacia = "true";

        let dibujando = false;

        const punto = e => {
            const r = canvas.getBoundingClientRect();
            return {
                x: (e.clientX - r.left) * canvas.width / r.width,
                y: (e.clientY - r.top) * canvas.height / r.height
            };
        };

        canvas.addEventListener("pointerdown", e => {
            dibujando = true;
            canvas.setPointerCapture(e.pointerId);
            const p = punto(e);
            contexto.beginPath();
            contexto.moveTo(p.x, p.y);
        });

        canvas.addEventListener("pointermove", e => {
            if (!dibujando) return;
            const p = punto(e);
            contexto.lineTo(p.x, p.y);
            contexto.stroke();
            canvas.dataset.vacia = "false";
        });

        const terminar = () => dibujando = false;
        canvas.addEventListener("pointerup", terminar);
        canvas.addEventListener("pointercancel", terminar);
    },

    limpiarFirma(canvas) {
        canvas.getContext("2d").clearRect(0, 0, canvas.width, canvas.height);
        canvas.dataset.vacia = "true";
    },

    leerFirma(canvas) {
        return canvas.dataset.vacia === "true"
            ? null
            : canvas.toDataURL("image/png").split(",")[1];
    }
};
