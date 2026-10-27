// En "Reservar cita", al cambiar paciente, médico o fecha se actualizan los horarios disponibles (sin perder lo ingresado).
// La fecha escrita con el teclado dispara "change" en cada cambio válido: se espera a que el usuario termine de escribir.
(function () {
    var DELAY_MS = 700;
    var form = document.getElementById('bookingForm');
    var refresh = document.getElementById('refreshTimes');
    if (!form || !refresh) return;
    document.body.classList.add('hide-with-js');

    var timer = null;
    function update() { form.requestSubmit(refresh); }

    form.querySelectorAll('[data-refresh]').forEach(function (field) {
        function onChange() {
            clearTimeout(timer);
            if (field.type === 'date') {
                timer = setTimeout(function () { timer = null; update(); }, DELAY_MS);
            } else {
                update();
            }
        }
        field.addEventListener('change', onChange);
        if (field.type === 'date') field.addEventListener('input', onChange); // ver nota en autosubmit.js
        field.addEventListener('blur', function () {
            if (field.type === 'date' && timer) {
                clearTimeout(timer);
                timer = null;
                update();
            }
        });
    });
})();
