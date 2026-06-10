const API = {
    listings: '/api/listings',
    scrape:   '/api/scrape/run',
    status:   '/api/scrape/status',
};

function getFilters() {
    return {
        source: document.getElementById('filter-source').value,
        isNew:  document.getElementById('filter-new').checked,
        hours:  document.getElementById('filter-hours').value,
    };
}

async function loadListings() {
    const { source, isNew, hours } = getFilters();
    const params = new URLSearchParams();

    if (source) params.set('source', source);
    if (isNew)  params.set('isNew', 'true');
    if (hours)  params.set('hours', hours);

    const grid = document.getElementById('listings-grid');
    grid.innerHTML = '<div class="loading">Loading...</div>';

    try {
        const res = await fetch(`${API.listings}?${params}`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const listings = await res.json();
        renderListings(listings);
    } catch (err) {
        grid.innerHTML = `<p class="empty">Failed to load listings: ${esc(err.message)}</p>`;
    }
}

async function loadStatus() {
    try {
        const res = await fetch(API.status);
        if (!res.ok) return;
        const runs = await res.json();
        renderStatus(runs);
    } catch {
        document.getElementById('scrape-status').textContent = 'Status unavailable.';
    }
}

async function triggerScrape() {
    const btn = document.getElementById('scrape-btn');
    const label = btn.textContent;
    btn.disabled = true;
    btn.textContent = 'Scraping...';

    try {
        const source = document.getElementById('filter-source').value;
        const qs = source ? `?source=${encodeURIComponent(source)}` : '';
        const res = await fetch(`${API.scrape}${qs}`, { method: 'POST' });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
    } catch (err) {
        alert(`Scrape failed: ${err.message}`);
    } finally {
        btn.disabled = false;
        btn.textContent = label;
        await Promise.all([loadListings(), loadStatus()]);
    }
}

function renderListings(listings) {
    const grid     = document.getElementById('listings-grid');
    const countEl  = document.getElementById('listing-count');

    countEl.textContent = `${listings.length} listing${listings.length !== 1 ? 's' : ''}`;

    if (!listings.length) {
        grid.innerHTML = '<p class="empty">No listings found.</p>';
        return;
    }

    grid.innerHTML = listings.map(l => {
        const thumbHtml = l.thumbnailUrl
            ? `<img src="${esc(l.thumbnailUrl)}" alt="" loading="lazy">`
            : `<div class="card__thumb-placeholder">No image</div>`;

        const priceHtml = formatPrice(l);

        const metaHtml = [
            l.location   ? `<span>${esc(l.location)}</span>` : '',
            l.publishedAt ? `<span>${formatDate(l.publishedAt)}</span>` : '',
        ].filter(Boolean).join('');

        return `
<a class="card${l.isNew ? ' card--new' : ''}" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer">
    <div class="card__thumb">${thumbHtml}</div>
    <div class="card__body">
        <div class="card__header">
            <span class="badge badge--${esc(l.source)}">${esc(l.source.toUpperCase())}</span>
            ${l.isNew ? '<span class="badge badge--new">NEW</span>' : ''}
        </div>
        <h3 class="card__title">${esc(l.title)}</h3>
        <div class="card__price">${priceHtml}</div>
        ${metaHtml ? `<div class="card__meta">${metaHtml}</div>` : ''}
    </div>
</a>`;
    }).join('');
}

function renderStatus(runs) {
    const el = document.getElementById('scrape-status');

    // Most recent successful run per source
    const lastBySource = {};
    for (const run of runs) {
        if (run.success && !lastBySource[run.source]) {
            lastBySource[run.source] = run;
        }
    }

    const entries = Object.entries(lastBySource);
    if (!entries.length) {
        el.textContent = 'No scrape runs recorded yet.';
        return;
    }

    el.innerHTML = entries
        .map(([src, run]) =>
            `<span><strong>${esc(src.toUpperCase())}</strong>: ` +
            `scraped ${formatDate(run.completedAt)} — ` +
            `${run.listingsFound} found, ${run.newListingsCount} new</span>`)
        .join(' &nbsp;&middot;&nbsp; ');
}

function formatPrice(l) {
    const suffix = l.isNegotiable ? ' (negotiable)' : '';
    if (l.priceBgn != null) return `${Number(l.priceBgn).toLocaleString('bg-BG')} lv${suffix}`;
    if (l.priceEur != null) return `${Number(l.priceEur).toLocaleString('bg-BG')} €${suffix}`;
    if (l.isNegotiable) return 'Negotiable';
    return '–';
}

function formatDate(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return '';

    const diffMs = Date.now() - d.getTime();
    const diffH  = diffMs / 3_600_000;

    if (diffH < 1)  return `${Math.max(1, Math.floor(diffMs / 60_000))}m ago`;
    if (diffH < 24) return `${Math.floor(diffH)}h ago`;
    return d.toLocaleDateString('bg-BG', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function esc(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g,  '&amp;')
        .replace(/</g,  '&lt;')
        .replace(/>/g,  '&gt;')
        .replace(/"/g,  '&quot;')
        .replace(/'/g,  '&#x27;');
}

document.getElementById('filter-source').addEventListener('change', loadListings);
document.getElementById('filter-new').addEventListener('change', loadListings);
document.getElementById('filter-hours').addEventListener('change', loadListings);
document.getElementById('scrape-btn').addEventListener('click', triggerScrape);

loadListings();
loadStatus();
