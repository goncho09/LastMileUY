export function escucharConexion(dotNetRef) {
    const alConectar = () => {
        dotNetRef.invokeMethodAsync("ConexionRestablecida")
            .catch(console.error);
    };

    window.addEventListener("online", alConectar);

    return {
        conectado: navigator.onLine,

        dispose: () => {
            window.removeEventListener("online", alConectar);
        }
    };
}