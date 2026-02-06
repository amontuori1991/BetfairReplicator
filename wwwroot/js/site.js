(function () {
    const loader = document.getElementById("bf-loader");
    if (!loader) return;

    const titleEl = loader.querySelector(".bf-loader-title");
    const subEl = loader.querySelector(".bf-loader-sub");

    function showLoader(label) {
        if (label) {
            // label breve = titolo / sub
            if (titleEl) titleEl.textContent = "Caricamento…";
            if (subEl) subEl.textContent = label;
        }
        loader.classList.add("is-visible");
        loader.setAttribute("aria-hidden", "false");
        loader.offsetHeight; // <-- aggiungi
    }

    function hideLoader() {
        loader.classList.remove("is-visible");
        loader.setAttribute("aria-hidden", "true");
    }

    // Se l'utente torna indietro (bfcache) o la pagina è già pronta, nascondo
    window.addEventListener("pageshow", function () {
        hideLoader();
    });

    // Intercetto click su link interni (navbar + tile + qualsiasi <a>)
    document.addEventListener("click", function (e) {
        const a = e.target.closest("a");
        if (!a) return;

        // ignorare: new tab, download, ancore, link esterni, javascript:
        if (e.defaultPrevented) return;
        if (e.button != null && e.button !== 0) return;
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if (a.hasAttribute("download")) return;

        const href = a.getAttribute("href") || "";
        if (!href || href.startsWith("#") || href.startsWith("javascript:")) return;

        // se target blank non mostrare
        if ((a.getAttribute("target") || "").toLowerCase() === "_blank") return;

        // link esterno? (diversa origin)
        try {
            const url = new URL(a.href, window.location.href);
            if (url.origin !== window.location.origin) return;
        } catch {
            return;
        }

        // Se è un link interno valido, mostra loader
        const label =
            a.getAttribute("data-loading") ||
            a.querySelector(".ns-tile-title")?.textContent?.trim() ||
            a.textContent?.trim() ||
            "Apro la sezione";


        showLoader(label);

    }, true);

    // Intercetto submit di form (es. Logout)
    document.addEventListener("submit", function (e) {
        const form = e.target;
        if (!form) return;

        const label =
            form.querySelector("button[type='submit']")?.textContent?.trim() ||
            "Operazione in corso";

        showLoader(label);
    }, true);
})();
