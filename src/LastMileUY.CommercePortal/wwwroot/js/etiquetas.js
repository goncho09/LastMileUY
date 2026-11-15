window.lastMileEtiquetas = {
    dibujar() {
        if (!window.JsBarcode) return false;
        document.querySelectorAll("svg[data-codigo]").forEach(svg => {
            JsBarcode(svg, svg.dataset.codigo, { format: "CODE128", height: 60, displayValue: true, fontSize: 14, margin: 0 });
        });
        return true;
    },
    imprimir() {
        window.print();
    }
};
