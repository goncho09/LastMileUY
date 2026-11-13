window.lastMileAlmacen = (() => {
    const BASE = "lastmileuy";
    const DATOS = "datos";
    const CLAVES = "claves";

    const abrir = () => new Promise((ok, mal) => {
        const pedido = indexedDB.open(BASE, 1);
        pedido.onupgradeneeded = () => {
            pedido.result.createObjectStore(DATOS);
            pedido.result.createObjectStore(CLAVES);
        };
        pedido.onsuccess = () => ok(pedido.result);
        pedido.onerror = () => mal(pedido.error);
    });

    const transaccion = (db, almacen, modo, accion) => new Promise((ok, mal) => {
        const t = db.transaction(almacen, modo);
        const pedido = accion(t.objectStore(almacen));
        t.oncomplete = () => ok(pedido.result);
        t.onerror = () => mal(t.error);
    });

    const conBase = async accion => {
        const db = await abrir();
        try {
            return await accion(db);
        } finally {
            db.close();
        }
    };

    async function clave(db) {
        let k = await transaccion(db, CLAVES, "readonly", s => s.get("principal"));

        if (!k) {
            k = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
            await transaccion(db, CLAVES, "readwrite", s => s.put(k, "principal"));
        }

        return k;
    }

    return {
        guardar: (nombre, texto) => conBase(async db => {
            const iv = crypto.getRandomValues(new Uint8Array(12));
            const cifrado = await crypto.subtle.encrypt(
                { name: "AES-GCM", iv }, await clave(db), new TextEncoder().encode(texto));

            await transaccion(db, DATOS, "readwrite", s => s.put({ iv, cifrado }, nombre));
        }),

        leer: nombre => conBase(async db => {
            const guardado = await transaccion(db, DATOS, "readonly", s => s.get(nombre));

            if (!guardado) return null;

            try {
                const plano = await crypto.subtle.decrypt(
                    { name: "AES-GCM", iv: guardado.iv }, await clave(db), guardado.cifrado);
                return new TextDecoder().decode(plano);
            } catch {
                return null;
            }
        }),

        borrar: nombre => conBase(db => transaccion(db, DATOS, "readwrite", s => s.delete(nombre))),

        borrarTodo: () => new Promise((ok, mal) => {
            const pedido = indexedDB.deleteDatabase(BASE);
            pedido.onsuccess = () => ok();
            pedido.onblocked = () => ok();
            pedido.onerror = () => mal(pedido.error);
        })
    };
})();
