// En "Reservar cita", al cambiar paciente, médico o fecha se actualizan los horarios disponibles (sin perder lo ingresado).
(function () {
    var form = document.getElementById('bookingForm');
    var refresh = document.getElementById('refreshTimes');
    if (!form || !refresh) return;
    document.body.classList.add('hide-with-js');
    form.querySelectorAll('[data-refresh]').forEach(function (field) {
        field.addEventListener('change', function () { form.requestSubmit(refresh); });
    });
})();
