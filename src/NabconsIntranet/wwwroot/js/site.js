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

    // Close the user menu and the notifications bell when clicking elsewhere.
    document.addEventListener('click', function (e) {
        document.querySelectorAll('details.usermenu[open], details.bell[open]').forEach(function (d) {
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

// ---------- business lead form: soft duplicate check before submitting ----------
(function () {
    var form = document.getElementById('lead-form');
    if (!form) return;
    var confirm = document.getElementById('ConfirmDuplicate'), box = document.getElementById('dup-live'), btn = document.getElementById('lead-submit');
    var checked = confirm.value === 'true';
    form.addEventListener('submit', function (e) {
        if (checked || !form.checkValidity()) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        var q = '&client=' + encodeURIComponent(form.querySelector('[name="Input.ClientName"]').value) +
                '&title=' + encodeURIComponent(form.querySelector('[name="Input.Title"]').value) +
                '&tender=' + encodeURIComponent(form.querySelector('[name="Input.TenderNumber"]').value);
        fetch(form.getAttribute('data-dup-check') + q, { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .catch(function () { return []; })
            .then(function (list) {
                checked = true;
                if (!list.length) { form.submit(); return; }
                confirm.value = 'true';
                box.textContent = '';
                var h = document.createElement('strong'); h.textContent = 'A similar lead may already exist: ';
                box.appendChild(h);
                box.appendChild(document.createTextNode(list.map(function (d) { return d.reference + ' (' + d.status + ', ' + d.submitted + ')'; }).join('; ') +
                    '. If yours is a different opportunity, click “Submit anyway”.'));
                box.classList.remove('hidden');
                btn.textContent = 'Submit anyway';
                box.scrollIntoView({ block: 'center' });
            });
    }, true);
})();

// ---------- lead status form: show reason / award fields for the chosen status ----------
document.querySelectorAll('select[data-status-select]').forEach(function (sel) {
    var form = sel.form;
    function sync() {
        form.querySelectorAll('[data-show-for]').forEach(function (box) {
            var show = box.getAttribute('data-show-for').split('|').indexOf(sel.value) >= 0;
            box.classList.toggle('hidden', !show);
            box.querySelectorAll('select, input').forEach(function (i) { if (i.name !== 'lossReasonText') i.required = show; });
        });
    }
    sel.addEventListener('change', sync); sync();
});

document.querySelectorAll('select[data-autosubmit]').forEach(function (s) { s.addEventListener('change', function () { s.form.submit(); }); });

// ---------- notifications: badge refresh by a small poll (no WebSockets / SSE), and the bell drop-down ----------
(function () {
    var body = document.body, seconds = parseInt(body.getAttribute('data-poll') || '0', 10);
    if (!seconds || !window.fetch) return;
    var interval = Math.max(30, seconds) * 1000, etag = body.getAttribute('data-poll-token') ? '"' + body.getAttribute('data-poll-token') + '"' : '';
    var inflight = false, errors = 0, timer = null, stopped = false;

    function setBadge(name, n) {
        document.querySelectorAll('[data-badge="' + name + '"]').forEach(function (el) {
            el.textContent = n > 99 ? '99+' : String(n);
            el.classList.toggle('hidden', !n);
        });
    }
    function apply(d) {
        ['unread', 'desk', 'inbox', 'admin', 'leads', 'enotes', 'board'].forEach(function (k) { if (typeof d[k] === 'number') setBadge(k, d[k]); });
        var s = document.getElementById('bell-summary');
        if (s) s.setAttribute('aria-label', 'Notifications, ' + d.unread + ' unread');
        document.dispatchEvent(new CustomEvent('nab:summary', { detail: d }));
    }
    function schedule(ms) { clearTimeout(timer); timer = setTimeout(tick, ms); }
    function tick() {
        timer = null;
        if (stopped || inflight) return;
        if (document.hidden) return;              // paused; resumes when the tab is shown again
        inflight = true;
        var headers = { 'Accept': 'application/json' };
        if (etag) headers['If-None-Match'] = etag;
        fetch('/api/notifications/summary', { credentials: 'same-origin', cache: 'no-store', headers: headers })
            .then(function (r) {
                if (r.status === 401 || r.status === 403) { stopped = true; return null; }   // signed out / session ended
                if (r.status === 304) return null;
                if (!r.ok) throw new Error('HTTP ' + r.status);
                etag = r.headers.get('ETag') || '';
                return r.json();
            })
            .then(function (d) { inflight = false; errors = 0; if (d) apply(d); if (!stopped) schedule(interval); })
            .catch(function () { inflight = false; errors++; if (!stopped) schedule(errors >= 3 ? 300000 : interval); });
    }
    document.addEventListener('visibilitychange', function () { if (!document.hidden && !timer && !inflight && !stopped) tick(); });
    schedule(interval);
})();

(function () {
    var bell = document.getElementById('bell');
    if (!bell) return;
    var summary = document.getElementById('bell-summary'), list = document.getElementById('bell-list');
    function items() { return Array.prototype.slice.call(bell.querySelectorAll('.bell-menu a[href], .bell-menu button')); }
    function load() {
        fetch('/Notifications?handler=Menu', { credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) { return r.ok ? r.text() : null; })
            .then(function (html) { if (html !== null) list.innerHTML = html; })     // server-rendered, HTML-encoded by Razor
            .catch(function () { /* keep the link to the Notifications page */ });
    }
    bell.addEventListener('toggle', function () { if (bell.open) load(); });
    bell.addEventListener('keydown', function (e) {
        if (!bell.open) return;
        if (e.key === 'Escape') { e.preventDefault(); bell.open = false; summary.focus(); return; }
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Home' && e.key !== 'End') return;
        var all = items(); if (!all.length) return;
        e.preventDefault();
        var i = all.indexOf(document.activeElement);
        if (e.key === 'Home') i = 0;
        else if (e.key === 'End') i = all.length - 1;
        else if (e.key === 'ArrowDown') i = i < 0 ? 0 : Math.min(all.length - 1, i + 1);
        else i = i <= 0 ? -1 : i - 1;
        (i < 0 ? summary : all[i]).focus();
    });
})();

// ---------- e-Note: "Mark to" type-ahead (the form also works without it: the typed name is resolved on the server) ----------
document.querySelectorAll('input[data-user-lookup]').forEach(function (input) {
    var hidden = document.getElementById(input.getAttribute('data-user-lookup'));
    var list = document.getElementById(input.getAttribute('aria-controls'));
    var chosen = document.getElementById('toChosen'), hint = chosen ? chosen.textContent : '';
    var items = [], active = -1, timer = null, seq = 0;
    if (!hidden || !list) return;
    function close() { list.classList.add('hidden'); input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); }
    function render() {
        list.textContent = '';
        items.forEach(function (u, i) {
            var li = document.createElement('li');
            li.id = 'lookup-' + u.id; li.setAttribute('role', 'option'); li.setAttribute('aria-selected', i === active ? 'true' : 'false');
            if (i === active) li.className = 'active';
            var s = document.createElement('strong'); s.textContent = u.name; li.appendChild(s);
            var m = document.createElement('span'); m.className = 'muted small'; m.textContent = [u.designation, u.vertical].filter(Boolean).join(' · '); li.appendChild(m);
            li.addEventListener('mousedown', function (e) { e.preventDefault(); pick(i); });
            list.appendChild(li);
        });
        if (!items.length) { var none = document.createElement('li'); none.className = 'muted small'; none.textContent = 'No active colleague matches.'; list.appendChild(none); }
        list.classList.remove('hidden'); input.setAttribute('aria-expanded', 'true');
        if (active >= 0) input.setAttribute('aria-activedescendant', 'lookup-' + items[active].id); else input.removeAttribute('aria-activedescendant');
    }
    function pick(i) {
        var u = items[i]; if (!u) return;
        hidden.value = u.id; input.value = u.name;
        if (chosen) chosen.textContent = 'Selected: ' + u.name + (u.designation ? ' — ' + u.designation : '') + (u.vertical ? ', ' + u.vertical : '');
        close();
    }
    input.addEventListener('input', function () {
        hidden.value = ''; if (chosen) chosen.textContent = hint;
        clearTimeout(timer);
        var q = input.value.trim();
        if (q.length < 2) { close(); return; }
        timer = setTimeout(function () {
            var mine = ++seq;
            fetch('/api/users/lookup?q=' + encodeURIComponent(q), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (d) { if (mine !== seq) return; items = d; active = d.length ? 0 : -1; render(); })
                .catch(function () { /* the server resolves the typed name instead */ });
        }, 200);
    });
    input.addEventListener('keydown', function (e) {
        if (list.classList.contains('hidden')) return;
        if (e.key === 'ArrowDown') { e.preventDefault(); active = Math.min(items.length - 1, active + 1); render(); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); active = Math.max(0, active - 1); render(); }
        else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); pick(active); }
        else if (e.key === 'Escape') { e.preventDefault(); close(); }
    });
    input.addEventListener('blur', function () { setTimeout(close, 150); });
});

// ---------- character counter for long text fields ----------
document.querySelectorAll('textarea[data-count]').forEach(function (t) {
    var out = document.getElementById(t.getAttribute('data-count'));
    if (!out) return;
    function sync() { out.textContent = t.value.length.toLocaleString('en-IN') + ' characters.'; }
    t.addEventListener('input', sync); sync();
});

// ---------- copy a value shown on the page (e.g. an attachment's SHA-256) ----------
document.querySelectorAll('[data-copy-text]').forEach(function (b) {
    b.addEventListener('click', function () {
        var label = b.textContent;
        nabCopy(b.getAttribute('data-copy-text'), function (ok) { b.textContent = ok ? 'Copied' : 'Select and press Ctrl+C'; setTimeout(function () { b.textContent = label; }, 2000); });
    });
});

// ---------- message board: preview chosen photos and check the count / size before sending ----------
document.querySelectorAll('input[type=file][data-multi-preview]').forEach(function (input) {
    var box = document.getElementById(input.getAttribute('data-multi-preview'));
    var max = parseInt(input.getAttribute('data-max-files') || '0', 10);
    input.addEventListener('change', function () {
        if (!box) return;
        box.textContent = '';
        if (max && input.files.length > max) { alert('Choose at most ' + max + ' photos for one post.'); input.value = ''; return; }
        Array.prototype.forEach.call(input.files, function (f, i) {
            var reader = new FileReader();
            reader.onload = function () { var img = document.createElement('img'); img.src = reader.result; img.alt = 'Selected photo ' + (i + 1); box.appendChild(img); };
            reader.readAsDataURL(f);     // data: URLs are allowed by the page's security policy
        });
    });
});
