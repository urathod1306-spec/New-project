// NABCONS Intranet - small, local, dependency-free script (no inline scripts are allowed by the CSP).
(function () {
    'use strict';
    // Ask for confirmation on buttons/links marked data-confirm="message".
    document.addEventListener('click', function (e) {
        var el = e.target.closest('[data-confirm]');
        if (el && !window.confirm(el.getAttribute('data-confirm'))) {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);

    // Prevent accidental double submission of forms.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (form.method && form.method.toLowerCase() === 'post') {
            if (form.dataset.submitted === '1') { e.preventDefault(); return; }
            form.dataset.submitted = '1';
            setTimeout(function () { form.dataset.submitted = ''; }, 8000);
        }
    });

    // Close the user menu when clicking elsewhere.
    document.addEventListener('click', function (e) {
        document.querySelectorAll('details.usermenu[open]').forEach(function (d) {
            if (!d.contains(e.target)) d.removeAttribute('open');
        });
    });

    // Show chosen file names for file inputs.
    document.querySelectorAll('input[type=file][data-max-mb]').forEach(function (input) {
        input.addEventListener('change', function () {
            var max = parseFloat(input.getAttribute('data-max-mb')) * 1024 * 1024;
            for (var i = 0; i < input.files.length; i++) {
                if (input.files[i].size > max) {
                    alert('"' + input.files[i].name + '" is larger than the ' + input.getAttribute('data-max-mb') + ' MB limit.');
                    input.value = '';
                    return;
                }
            }
        });
    });
})();

document.querySelectorAll('[data-print]').forEach(function (b) { b.addEventListener('click', function () { window.print(); }); });

// A drop-down with data-other-toggle="<id>" shows that block only when "Others" is chosen.
document.querySelectorAll('select[data-other-toggle]').forEach(function (s) {
    var box = document.getElementById(s.getAttribute('data-other-toggle'));
    function sync() {
        var other = s.value === '__other__';
        box.classList.toggle('hidden', !other);
        var input = box.querySelector('input'); if (input) { input.required = other; if (other && document.activeElement === s) input.focus(); }
    }
    s.addEventListener('change', sync); sync();
});

// Mobile-number fields: allow digits only.
document.querySelectorAll('input[data-digits-only]').forEach(function (i) {
    i.addEventListener('input', function () {
        var v = i.value.replace(/\D/g, '');
        if (v.length > 10 && v.indexOf('91') === 0) v = v.slice(2);   // pasted +91 98200 12345
        if (v.length > 10 && v.charAt(0) === '0') v = v.slice(1);      // pasted 098200 12345
        v = v.slice(0, 10);
        if (v !== i.value) i.value = v;
    });
});
