// Pide confirmación antes de enviar formularios marcados con data-confirm (por ejemplo, cancelar una cita).
document.querySelectorAll('form[data-confirm]').forEach(function (form) {
    form.addEventListener('submit', function (event) {
        if (!window.confirm(form.dataset.confirm)) event.preventDefault();
    });
});
