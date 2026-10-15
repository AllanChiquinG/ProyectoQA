// Oculta el aviso temporal igual que el prototipo (3,5 s).
setTimeout(function () {
    var toast = document.getElementById('toast');
    if (toast) toast.style.display = 'none';
}, 3500);
