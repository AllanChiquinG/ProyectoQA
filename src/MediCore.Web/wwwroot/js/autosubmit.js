// Envía el formulario al cambiar fecha, médico u otro campo marcado, igual que el prototipo actualizaba la vista al instante.
document.querySelectorAll('form[data-autosubmit]').forEach(function (form) {
    form.addEventListener('change', function () { form.requestSubmit(); });
});
