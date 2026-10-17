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

// ---------- photo preview (My Profile) ----------
document.querySelectorAll('input[type=file][data-preview]').forEach(function (input) {
    input.addEventListener('change', function () {
        var img = document.getElementById(input.getAttribute('data-preview'));
        var current = document.getElementById('photo-current');
        if (!img || !input.files.length) return;
        var reader = new FileReader();
        reader.onload = function () { img.src = reader.result; img.classList.remove('hidden'); if (current) current.classList.add('hidden'); };
        reader.readAsDataURL(input.files[0]);
    });
});

// ---------- copy to clipboard (works on plain-HTTP intranet addresses too) ----------
function nabCopy(text, done) {
    function fallback() {
        var ta = document.createElement('textarea');
        ta.value = text; ta.setAttribute('readonly', ''); ta.className = 'offscreen';
        document.body.appendChild(ta); ta.select();
        var ok = false; try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(ta); done(ok);
    }
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(function () { done(true); }, fallback);
    else fallback();
}

// ---------- staff directory contact card ----------
(function () {
    var dlg = document.getElementById('contact-card');
    if (!dlg) return;
    var opener = null, data = null;
    var status = document.getElementById('cc-status');

    function fill(d) {
        data = d;
        document.getElementById('cc-name').textContent = d.name || '';
        document.getElementById('cc-designation').textContent = d.designation || '';
        var photo = document.getElementById('cc-photo'), initials = document.getElementById('cc-initials');
        if (d.photo) { photo.src = d.photo; photo.alt = 'Photo of ' + d.name; photo.classList.remove('hidden'); initials.classList.add('hidden'); }
        else { photo.classList.add('hidden'); photo.removeAttribute('src'); initials.textContent = d.initials || ''; initials.classList.remove('hidden'); }
        dlg.querySelectorAll('[data-cc]').forEach(function (el) {
            var key = el.getAttribute('data-cc'), v = d[key] || '';
            el.textContent = v;
            if (el.hasAttribute('data-cc-href')) el.setAttribute('href', el.getAttribute('data-cc-href') + v.replace(/\s+/g, ''));
        });
        dlg.querySelectorAll('[data-cc-row]').forEach(function (row) { row.classList.toggle('hidden', !d[row.getAttribute('data-cc-row')]); });
        document.getElementById('cc-vcard').setAttribute('href', d.vcard);
        status.textContent = '';
    }

    function open(row) {
        opener = row;
        fetch('/directory/card/' + encodeURIComponent(row.getAttribute('data-card')), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (d) { fill(d); dlg.showModal(); var c = dlg.querySelector('.cc-close'); if (c) c.focus(); })
            .catch(function () { alert('The contact card could not be loaded. Please try again.'); });
    }
    function close() { if (dlg.open) dlg.close(); }

    dlg.addEventListener('close', function () { if (opener) opener.focus(); });
    dlg.addEventListener('click', function (e) {
        if (e.target === dlg) close();                       // click on the backdrop
        if (e.target.closest('[data-cc-close]')) close();
        var copy = e.target.closest('[data-copy]');
        if (copy && data) {
            var v = data[copy.getAttribute('data-copy')] || '';
            nabCopy(v, function (ok) { status.textContent = ok ? 'Copied: ' + v : 'Could not copy — select the text and press Ctrl+C.'; });
        }
    });
    // keep keyboard focus inside the card
    dlg.addEventListener('keydown', function (e) {
        if (e.key !== 'Tab') return;
        var items = Array.prototype.filter.call(dlg.querySelectorAll('a[href], button'), function (el) { return el.offsetParent !== null; });
        if (!items.length) return;
        var first = items[0], last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });
    document.querySelectorAll('[data-card]').forEach(function (row) {
        row.addEventListener('click', function (e) { if (e.target.closest('a, button, [data-no-card]')) return; open(row); });
        row.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(row); } });
    });
})();
