(() => {
    const campo = document.getElementById("escaneo");
    const aviso = document.getElementById("aviso-escaneo");
    const sobrantes = document.getElementById("sobrantes");

    if (!campo) return;

    campo.addEventListener("keydown", (evento) => {
        if (evento.key !== "Enter") return;

        evento.preventDefault();
        const codigo = campo.value.trim().toUpperCase();
        campo.value = "";

        if (!codigo || codigo.length > 40) return;

        const casilla = document.querySelector(`input[data-codigo="${CSS.escape(codigo)}"]`);

        if (casilla) {
            casilla.checked = true;
            aviso.textContent = `${codigo} recibido.`;
            aviso.className = "mt-1.5 text-sm text-emerald-300";
            return;
        }

        if (!sobrantes.querySelector(`input[value="${CSS.escape(codigo)}"]`)) {
            const oculto = document.createElement("input");
            oculto.type = "hidden";
            oculto.name = "Recibidos";
            oculto.value = codigo;
            sobrantes.appendChild(oculto);
        }

        aviso.textContent = `${codigo} no es de esta ruta: queda anotado como sobrante.`;
        aviso.className = "mt-1.5 text-sm text-amber-300";
    });
})();
