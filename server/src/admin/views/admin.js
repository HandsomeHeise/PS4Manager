const status = document.querySelector("#status");
const messages = {
  invalid_credentials:
    "Anmeldung fehlgeschlagen. Prüfe Passwort und einen neuen Authenticator-Code.",
  invalid_csrf:
    "Die Sitzung ist abgelaufen. Lade die Seite neu und melde dich bei Bedarf wieder an.",
  authentication_required: "Bitte erneut anmelden.",
  immutable_release: "Dieser Release ist unveränderlich. Lege einen neuen Entwurf an.",
  invalid_package: "Das ZIP-Paket ist ungültig. Prüfe Manifest und Dateipfade.",
  invalid_preview: "Dateiinhalt und Medientyp stimmen nicht überein.",
  already_exists: "Dieser Name, diese Version oder Medienposition ist bereits vergeben.",
  payload_too_large: "Die Datei überschreitet das Upload-Limit.",
  package_type_mismatch: "Der Pakettyp im Manifest stimmt nicht mit dem Entwurf überein.",
  rate_limited: "Zu viele Anmeldeversuche. Warte eine Minute.",
  artifact_integrity_failure:
    "Die Paketdatei fehlt oder ihre Signatur ist ungültig. Wiederherstellung abgebrochen."
};
for (const form of document.querySelectorAll("form")) {
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = form.querySelector("button");
    button.disabled = true;
    status.className = "";
    status.textContent = "Wird verarbeitet …";
    try {
      let csrf = document.querySelector('meta[name="csrf-token"]').content;
      if (form.hasAttribute("data-login")) {
        const pre = await fetch("/api/admin/login-token", { credentials: "same-origin" });
        if (!pre.ok)
          throw new Error("Anmeldung momentan nicht möglich. Versuche es später erneut.");
        csrf = (await pre.json()).csrfToken;
      }
      const values = new FormData(form),
        headers = { "x-csrf-token": csrf };
      let url = form.action,
        body;
      if (form.hasAttribute("data-upload")) {
        body = new FormData();
        body.set(form.hasAttribute("data-update") ? "package" : "file", values.get("file"));
        if (form.hasAttribute("data-update")) body.set("manifest", values.get("manifest"));
        if (form.hasAttribute("data-media"))
          url += `?kind=${encodeURIComponent(values.get("kind"))}&position=${encodeURIComponent(values.get("position"))}`;
      } else {
        const payload = Object.fromEntries(values);
        for (const field of ["compatibility", "order"]) {
          if (!(field in payload)) continue;
          try {
            payload[field] = JSON.parse(payload[field]);
          } catch {
            throw new Error(
              "Kompatibilitätsliste und Medienreihenfolge müssen gültiges JSON enthalten."
            );
          }
        }
        body = JSON.stringify(payload);
        headers["content-type"] = "application/json";
      }
      const response = await fetch(url, {
        method: form.dataset.method || "POST",
        credentials: "same-origin",
        headers,
        body
      });
      const result = response.status === 204 ? {} : await response.json();
      if (!response.ok)
        throw new Error(
          messages[result.error] ||
            `Speichern fehlgeschlagen (HTTP ${response.status}). Prüfe die Eingaben und versuche es erneut.`
        );
      location.assign(
        form.dataset.redirect
          ? form.dataset.redirect.replace("{id}", encodeURIComponent(result.id))
          : location.pathname
      );
    } catch (error) {
      status.className = "error";
      status.textContent = error.message;
      button.disabled = false;
    }
  });
}
