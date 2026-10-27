// Envía el formulario al cambiar fecha, médico u otro campo marcado, como el prototipo.
// Las listas se envían al instante. La fecha escrita con el teclado dispara "change" en cada cambio válido (por ejemplo,
// al teclear el primer dígito del año), así que se espera a que el usuario termine antes de enviar.
(function () {
    var DELAY_MS = 700;

    document.querySelectorAll('form[data-autosubmit]').forEach(function (form) {
        var timer = null;
        function onChange(event) {
            clearTimeout(timer);
            if (event.target.type === 'date') {
                timer = setTimeout(function () { timer = null; form.requestSubmit(); }, DELAY_MS);
            } else {
                form.requestSubmit();
            }
        }
        form.addEventListener('change', onChange);
        // Algunos navegadores y herramientas solo emiten "input" al escribir la fecha; se atiende igual, con espera.
        form.addEventListener('input', function (event) { if (event.target.type === 'date') onChange(event); });
        // Al salir del campo de fecha con un cambio pendiente, se envía sin esperar.
        form.addEventListener('focusout', function (event) {
            if (event.target.type === 'date' && timer) {
                clearTimeout(timer);
                timer = null;
                form.requestSubmit();
            }
        });
    });
})();
