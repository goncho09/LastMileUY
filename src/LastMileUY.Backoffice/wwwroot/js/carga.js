(() => {
    const campo = document.getElementById("escaneo");
    const aviso = document.getElementById("aviso-escaneo");

    if (!campo) return;

    campo.addEventListener("keydown", (evento) => {
        if (evento.key !== "Enter") return;

        evento.preventDefault();
        const codigo = campo.value.trim().toUpperCase();
        campo.value = "";

        if (!codigo) return;

        const casilla = document.querySelector(`input[data-codigo="${CSS.escape(codigo)}"]`);

        if (!casilla) {
            aviso.textContent = `${codigo} no es de esta ruta.`;
            aviso.className = "text-sm text-red-300";
            return;
        }

        casilla.checked = true;
        aviso.textContent = `${codigo} cargado.`;
        aviso.className = "text-sm text-emerald-300";
    });
})();
