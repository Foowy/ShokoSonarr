const API_BASE = window.location.pathname.replace(/\/dashboard.*$/, '').replace('/api/plugin/ShokoSonarr', '/api/v1.0/ShokoSonarr');

const THEME_STORAGE_KEY = 'shoko-sonarr-theme';
const systemDarkQuery = window.matchMedia('(prefers-color-scheme: dark)');

function resolveTheme(pref) {
  return pref === 'system' ? (systemDarkQuery.matches ? 'ember-dark' : 'paper-light') : pref;
}

function applyTheme(pref) {
  document.documentElement.dataset.theme = resolveTheme(pref);
}

function initTheme() {
  const pref = localStorage.getItem(THEME_STORAGE_KEY) || 'system';
  document.getElementById('theme-select').value = pref;
  applyTheme(pref);
  systemDarkQuery.addEventListener('change', () => {
    if ((localStorage.getItem(THEME_STORAGE_KEY) || 'system') === 'system')
      applyTheme('system');
  });
}

document.getElementById('theme-select').onchange = (e) => {
  localStorage.setItem(THEME_STORAGE_KEY, e.target.value);
  applyTheme(e.target.value);
};

async function fetchJson(path, options) {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  return res.status === 204 ? null : res.json();
}

const MAX_TICKS = 24;

function buildStrip(count) {
  const strip = document.createElement('div');
  strip.className = 'strip';
  const shown = Math.min(count, MAX_TICKS);
  for (let i = 0; i < shown; i++) {
    const tick = document.createElement('span');
    tick.className = 'tick';
    strip.appendChild(tick);
  }
  if (count > MAX_TICKS) {
    const overflow = document.createElement('span');
    overflow.className = 'tick overflow';
    overflow.textContent = `+${count - MAX_TICKS}`;
    strip.appendChild(overflow);
  }
  return strip;
}

const bulkSelectedIds = new Set();
let lastSeriesList = [];

function renderSeries(snapshot) {
  const container = document.getElementById('series-list');
  // Re-rendering rebuilds every row from scratch, which would otherwise collapse anything the user
  // had expanded (e.g. on every Live Refresh tick) -- carry the expanded set across the rebuild.
  const expandedIds = new Set([...container.querySelectorAll('.series-row.expanded')].map(r => r.dataset.seriesId));
  container.innerHTML = '';
  lastSeriesList = (snapshot && snapshot.Data) ? snapshot.Data.Series : [];
  // Bulk selection is intentionally preserved across re-renders (e.g. Live Refresh ticks) the same
  // way expanded rows are -- but drop any selected ID no longer present in the fresh snapshot.
  for (const id of [...bulkSelectedIds])
    if (!lastSeriesList.some(s => String(s.ShokoSeriesId) === id)) bulkSelectedIds.delete(id);
  updateBulkActionsBar();

  if (!snapshot || !snapshot.Data || snapshot.Data.Series.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = 'No missing episodes found.';
    container.appendChild(empty);
    return;
  }

  for (const series of snapshot.Data.Series) {
    const row = document.createElement('div');
    row.className = 'series-row' + (series.TvdbId ? '' : ' no-match');
    row.dataset.seriesId = series.ShokoSeriesId;
    if (expandedIds.has(String(series.ShokoSeriesId))) row.classList.add('expanded');

    const header = document.createElement('div');
    header.className = 'header';

    const select = document.createElement('input');
    select.type = 'checkbox';
    select.className = 'bulk-select';
    select.checked = bulkSelectedIds.has(String(series.ShokoSeriesId));
    select.onclick = (e) => {
      e.stopPropagation();
      const id = String(series.ShokoSeriesId);
      if (select.checked) bulkSelectedIds.add(id); else bulkSelectedIds.delete(id);
      updateBulkActionsBar();
    };
    header.appendChild(select);

    const chevron = document.createElement('span');
    chevron.className = 'chevron';
    chevron.textContent = '▸';
    header.appendChild(chevron);

    const title = document.createElement('span');
    title.className = 'title';
    title.textContent = series.Title;
    header.appendChild(title);

    header.appendChild(buildStrip(series.MissingEpisodes.length));

    const count = document.createElement('span');
    count.className = 'count';
    count.textContent = `${series.MissingEpisodes.length} ep`;
    header.appendChild(count);

    header.onclick = () => row.classList.toggle('expanded');
    row.appendChild(header);

    const episodesDiv = document.createElement('div');
    episodesDiv.className = 'episodes';
    for (const ep of series.MissingEpisodes) {
      const epRow = document.createElement('div');
      epRow.className = 'episode-row';
      const code = ep.IsSpecial ? `S${ep.EpisodeNumber}` : `E${ep.EpisodeNumber}`;
      epRow.innerHTML = `<span><span class="ep-code">${code}</span><span class="ep-title">${ep.Title || '(untitled)'}</span></span><span class="status ${ep.ActionStatus}">${ep.ActionStatus}</span>`;
      episodesDiv.appendChild(epRow);
    }
    row.appendChild(episodesDiv);

    const rowActions = document.createElement('div');
    rowActions.className = 'row-actions';
    if (series.TvdbId) {
      const searchBtn = document.createElement('button');
      searchBtn.className = 'primary';
      searchBtn.textContent = 'Add to Sonarr / Search';
      searchBtn.onclick = () => addAndSearch(series);
      rowActions.appendChild(searchBtn);
    } else {
      const findMatchBtn = document.createElement('button');
      findMatchBtn.textContent = 'Find Match';
      findMatchBtn.onclick = () => findMatchForSeries(series, rowActions, findMatchBtn);
      rowActions.appendChild(findMatchBtn);
    }

    const specialsToggle = document.createElement('div');
    specialsToggle.className = 'specials-toggle';
    const currentOverride = series.IncludeSpecialsOverride === null || series.IncludeSpecialsOverride === undefined
      ? null : series.IncludeSpecialsOverride;
    for (const [label, value, tooltip] of [
      ['Include', true, 'Always include specials episodes for this series, regardless of the global setting'],
      ['Exclude', false, 'Always exclude specials episodes for this series, regardless of the global setting'],
    ]) {
      const btn = document.createElement('button');
      btn.textContent = label;
      btn.title = tooltip;
      const isActive = currentOverride === value;
      if (isActive) btn.classList.add('active');
      // Clicking the already-active button clears the override back to the global default.
      btn.onclick = (e) => { e.stopPropagation(); setSpecialsOverride(series.ShokoSeriesId, isActive ? null : value); };
      specialsToggle.appendChild(btn);
    }
    if (currentOverride === null) specialsToggle.title = 'Following the global specials setting';
    rowActions.appendChild(specialsToggle);

    const profileToggle = document.createElement('button');
    profileToggle.textContent = 'Profile';
    profileToggle.title = (series.QualityProfileIdOverride || series.RootFolderPathOverride)
      ? 'This series has a Sonarr profile/root-folder override set'
      : 'Set a per-series Sonarr quality profile / root folder override';
    if (series.QualityProfileIdOverride || series.RootFolderPathOverride) profileToggle.classList.add('active');
    profileToggle.onclick = (e) => {
      e.stopPropagation();
      toggleProfileEditor(row, series);
    };
    rowActions.appendChild(profileToggle);

    row.appendChild(rowActions);

    container.appendChild(row);
  }
}

async function addAndSearch(series) {
  const anidbEpisodeIds = series.MissingEpisodes.map(e => e.AnidbEpisodeId);
  const result = await fetchJson('/Sonarr/add-and-search', {
    method: 'POST',
    body: JSON.stringify({ shokoSeriesId: series.ShokoSeriesId, tvdbId: series.TvdbId, anidbEpisodeIds }),
  });
  alert(result.Success ? 'Search triggered.' : `Failed: ${result.Message}`);
  await loadScanResults();
}

async function toggleProfileEditor(row, series) {
  const existingEditor = row.querySelector('.profile-editor');
  if (existingEditor) {
    existingEditor.remove();
    return;
  }

  const editor = document.createElement('div');
  editor.className = 'profile-editor';
  editor.innerHTML = `
    <label>Quality Profile <select class="profile-editor-quality"><option value="">Use global default</option></select></label>
    <label>Root Folder <select class="profile-editor-root"><option value="">Use global default</option></select></label>
    <button class="profile-editor-save primary">Save</button>
  `;
  row.appendChild(editor);

  const stored = await fetchJson('/Settings');
  const settings = { baseUrl: stored.Data.BaseUrl, apiKey: '', scanIntervalHours: 0, includeSpecials: false, hideUnaired: false, notificationWebhookUrl: '' };
  const options = await fetchJson('/Settings/sonarr-options', { method: 'POST', body: JSON.stringify(settings) });
  if (options.Success) {
    const qualitySelect = editor.querySelector('.profile-editor-quality');
    for (const p of options.Data.qualityProfiles || []) {
      const opt = document.createElement('option');
      opt.value = p.Id;
      opt.textContent = p.Name;
      if (series.QualityProfileIdOverride === p.Id) opt.selected = true;
      qualitySelect.appendChild(opt);
    }
    const rootSelect = editor.querySelector('.profile-editor-root');
    for (const f of options.Data.rootFolders || []) {
      const opt = document.createElement('option');
      opt.value = f.Path;
      opt.textContent = f.Path;
      if (series.RootFolderPathOverride === f.Path) opt.selected = true;
      rootSelect.appendChild(opt);
    }
  }

  editor.querySelector('.profile-editor-save').onclick = async (e) => {
    e.stopPropagation();
    const qualityProfileId = Number(editor.querySelector('.profile-editor-quality').value) || null;
    const rootFolderPath = editor.querySelector('.profile-editor-root').value || null;
    const result = await fetchJson(`/Scan/series/${series.ShokoSeriesId}/sonarr-override`, {
      method: 'PUT',
      body: JSON.stringify({ qualityProfileId, rootFolderPath }),
    });
    renderSeries(result);
  };
}

function updateBulkActionsBar() {
  const bar = document.getElementById('bulk-actions-bar');
  const count = bulkSelectedIds.size;
  bar.classList.toggle('hidden', count === 0);
  document.getElementById('bulk-selected-count').textContent = `${count} selected`;
}

document.getElementById('bulk-select-all').onclick = () => {
  for (const s of lastSeriesList) bulkSelectedIds.add(String(s.ShokoSeriesId));
  renderSeries({ Data: { Series: lastSeriesList } });
};

document.getElementById('bulk-clear').onclick = () => {
  bulkSelectedIds.clear();
  renderSeries({ Data: { Series: lastSeriesList } });
};

async function bulkSetSpecialsOverride(includeSpecials) {
  const ids = [...bulkSelectedIds];
  let result = null;
  for (const id of ids) {
    result = await fetchJson(`/Scan/series/${id}/include-specials`, {
      method: 'PUT',
      body: JSON.stringify({ includeSpecials }),
    });
  }
  bulkSelectedIds.clear();
  if (result) renderSeries(result);
}

document.getElementById('bulk-include-specials').onclick = () => bulkSetSpecialsOverride(true);
document.getElementById('bulk-exclude-specials').onclick = () => bulkSetSpecialsOverride(false);

document.getElementById('bulk-add-and-search').onclick = async () => {
  const selected = lastSeriesList.filter(s => bulkSelectedIds.has(String(s.ShokoSeriesId)));
  const withMatch = selected.filter(s => s.TvdbId);
  const skipped = selected.length - withMatch.length;
  let triggered = 0, failed = 0;
  for (const series of withMatch) {
    const anidbEpisodeIds = series.MissingEpisodes.map(e => e.AnidbEpisodeId);
    const result = await fetchJson('/Sonarr/add-and-search', {
      method: 'POST',
      body: JSON.stringify({ shokoSeriesId: series.ShokoSeriesId, tvdbId: series.TvdbId, anidbEpisodeIds }),
    });
    if (result.Success) triggered++; else failed++;
  }
  alert(`Triggered ${triggered}, skipped ${skipped} with no Sonarr match, ${failed} failed.`);
  bulkSelectedIds.clear();
  await loadScanResults();
};

async function setSpecialsOverride(shokoSeriesId, includeSpecials) {
  const result = await fetchJson(`/Scan/series/${shokoSeriesId}/include-specials`, {
    method: 'PUT',
    body: JSON.stringify({ includeSpecials }),
  });
  renderSeries(result);
}

async function loadScanResults() {
  const result = await fetchJson('/Scan/results');
  renderSeries(result);
}

const LIVE_REFRESH_STORAGE_KEY = 'shoko-sonarr-live-refresh';
const LIVE_REFRESH_INTERVAL_MS = 20_000;
let liveRefreshTimer = null;

function setLiveRefresh(enabled) {
  localStorage.setItem(LIVE_REFRESH_STORAGE_KEY, enabled ? '1' : '0');
  clearInterval(liveRefreshTimer);
  liveRefreshTimer = null;
  if (enabled) {
    // Just re-reads the last saved snapshot (cheap LiteDB read) — never triggers a rescan or Sonarr calls.
    liveRefreshTimer = setInterval(() => { if (!document.hidden) loadScanResults(); }, LIVE_REFRESH_INTERVAL_MS);
  }
}

document.getElementById('live-refresh').onchange = (e) => setLiveRefresh(e.target.checked);

const HEALTH_CHECK_INTERVAL_MS = 60_000;

async function checkConnectionHealth() {
  const el = document.getElementById('connection-health');
  const label = el.querySelector('.health-label');
  const result = await fetchJson('/Settings/health');
  el.classList.remove('ok', 'err');
  if (result.Success) {
    el.classList.add('ok');
    label.textContent = 'Sonarr Connected';
  } else {
    el.classList.add('err');
    label.textContent = 'Sonarr Unreachable';
  }
}

checkConnectionHealth();
setInterval(() => { if (!document.hidden) checkConnectionHealth(); }, HEALTH_CHECK_INTERVAL_MS);

let savedQualityProfileId = null;
let savedRootFolderPath = null;
let savedRadarrQualityProfileId = null;
let savedRadarrRootFolderPath = null;

function currentSettingsForm() {
  return {
    baseUrl: document.getElementById('settings-url').value,
    apiKey: document.getElementById('settings-key').value,
    scanIntervalHours: Number(document.getElementById('settings-interval').value),
    includeSpecials: document.getElementById('settings-include-specials').checked,
    hideUnaired: document.getElementById('settings-hide-unaired').checked,
    notificationWebhookUrl: document.getElementById('settings-webhook').value,
  };
}

function populateSelect(id, items, valueKey, labelKey, selectedValue) {
  const select = document.getElementById(id);
  select.innerHTML = '';
  for (const item of items || []) {
    const option = document.createElement('option');
    option.value = item[valueKey];
    option.textContent = item[labelKey];
    if (String(item[valueKey]) === String(selectedValue))
      option.selected = true;
    select.appendChild(option);
  }
}

async function loadSonarrOptions(settings) {
  const result = await fetchJson('/Settings/sonarr-options', { method: 'POST', body: JSON.stringify(settings) });
  if (!result.Success) {
    setStatus(`Failed to load Sonarr options: ${result.Message}`, false);
    return;
  }
  populateSelect('settings-quality-profile', result.Data.qualityProfiles, 'Id', 'Name', savedQualityProfileId);
  populateSelect('settings-root-folder', result.Data.rootFolders, 'Path', 'Path', savedRootFolderPath);
}

async function loadRadarrOptions(settings) {
  const result = await fetchJson('/RadarrSettings/radarr-options', { method: 'POST', body: JSON.stringify(settings) });
  if (!result.Success) {
    setStatus(`Failed to load Radarr options: ${result.Message}`, false);
    return;
  }
  populateSelect('radarr-settings-quality-profile', result.Data.qualityProfiles, 'Id', 'Name', savedRadarrQualityProfileId);
  populateSelect('radarr-settings-root-folder', result.Data.rootFolders, 'Path', 'Path', savedRadarrRootFolderPath);
}

async function loadSettings() {
  const result = await fetchJson('/Settings');
  document.getElementById('settings-url').value = result.Data.BaseUrl || '';
  // Never populate the actual key into .value: the field must stay blank so save-with-no-key
  // (currentSettingsForm's apiKey: '') keeps hitting the backend's "preserve existing key" path.
  // The placeholder is just a visual "a key is saved" indicator, not the key itself.
  document.getElementById('settings-key').placeholder = result.Data.ApiKey ? 'Key saved (leave blank to keep)' : '';
  document.getElementById('settings-webhook').placeholder = result.Data.NotificationWebhookUrl ? 'Webhook saved (leave blank to keep)' : '';
  document.getElementById('settings-interval').value = result.Data.ScanIntervalHours;
  document.getElementById('settings-include-specials').checked = result.Data.IncludeSpecials;
  document.getElementById('settings-hide-unaired').checked = result.Data.HideUnaired;
  savedQualityProfileId = result.Data.QualityProfileId;
  savedRootFolderPath = result.Data.RootFolderPath;
  populateSelect('settings-quality-profile', savedQualityProfileId ? [{ Id: savedQualityProfileId, Name: `#${savedQualityProfileId}` }] : [], 'Id', 'Name', savedQualityProfileId);
  populateSelect('settings-root-folder', savedRootFolderPath ? [{ Path: savedRootFolderPath }] : [], 'Path', 'Path', savedRootFolderPath);
  // The stored API key is masked here, not usable to call Sonarr — dropdowns above show the saved
  // value as a placeholder option (name resolved server-side below, or falls back to the bare ID);
  // Test Connection (re-entering the real key) repopulates them with the full live list from Sonarr.
  if (savedQualityProfileId) {
    const profileResult = await fetchJson('/Settings/quality-profile');
    if (profileResult.Success)
      populateSelect('settings-quality-profile', [profileResult.Data], 'Id', 'Name', savedQualityProfileId);
    else
      setStatus(`Showing profile #${savedQualityProfileId} — couldn't resolve its name: ${profileResult.Message}`, false);
  }

  const radarrResult = await fetchJson('/RadarrSettings');
  document.getElementById('radarr-settings-url').value = radarrResult.Data.BaseUrl || '';
  document.getElementById('radarr-settings-key').placeholder = radarrResult.Data.ApiKey ? 'Key saved (leave blank to keep)' : '';
  savedRadarrQualityProfileId = radarrResult.Data.QualityProfileId;
  savedRadarrRootFolderPath = radarrResult.Data.RootFolderPath;
  populateSelect('radarr-settings-quality-profile', savedRadarrQualityProfileId ? [{ Id: savedRadarrQualityProfileId, Name: `#${savedRadarrQualityProfileId}` }] : [], 'Id', 'Name', savedRadarrQualityProfileId);
  populateSelect('radarr-settings-root-folder', savedRadarrRootFolderPath ? [{ Path: savedRadarrRootFolderPath }] : [], 'Path', 'Path', savedRadarrRootFolderPath);
}

document.getElementById('scan-now').onclick = async () => {
  const result = await fetchJson('/Scan', { method: 'POST' });
  renderSeries(result);
};

document.getElementById('sync-tags').onclick = async () => {
  const result = await fetchJson('/Sonarr/sync-tags', { method: 'POST' });
  if (!result.Success) {
    alert(`Failed: ${result.Message}`);
    return;
  }
  const { Updated, SkippedNoMatch, Failed } = result.Data;
  alert(`Tagged ${Updated}, skipped ${SkippedNoMatch} with no Sonarr match, ${Failed} failed.`);
};

document.getElementById('open-settings').onclick = () => {
  document.getElementById('settings-panel').classList.toggle('hidden');
};

function renderPending(entries) {
  const container = document.getElementById('pending-list');
  container.innerHTML = '';
  if (!entries || entries.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = 'No pending Sonarr searches.';
    container.appendChild(empty);
    return;
  }
  for (const entry of entries) {
    const row = document.createElement('div');
    row.className = 'pending-row';
    const meta = document.createElement('span');
    meta.className = 'pending-meta';
    const seriesLabel = entry.SeriesTitle || `Series #${entry.ShokoSeriesId}`;
    const episodeLabel = entry.EpisodeTitle || `AniDB ep ${entry.AnidbEpisodeId}`;
    meta.textContent = `${seriesLabel} · ${episodeLabel} · triggered ${new Date(entry.TriggeredAtUtc).toLocaleString()}`;
    row.appendChild(meta);
    const cancelBtn = document.createElement('button');
    cancelBtn.textContent = 'Cancel';
    cancelBtn.onclick = () => cancelPending(entry.ShokoSeriesId, entry.AnidbEpisodeId);
    row.appendChild(cancelBtn);
    container.appendChild(row);
  }
}

async function loadPending() {
  const result = await fetchJson('/Scan/pending');
  renderPending(result.Data);
}

async function cancelPending(shokoSeriesId, anidbEpisodeId) {
  const result = await fetchJson(`/Scan/pending/${shokoSeriesId}/${anidbEpisodeId}`, { method: 'DELETE' });
  renderPending(result.Data);
}

document.getElementById('open-pending').onclick = () => {
  document.getElementById('pending-panel').classList.toggle('hidden');
  if (!document.getElementById('pending-panel').classList.contains('hidden'))
    loadPending();
};

function renderHistory(entries) {
  const container = document.getElementById('history-list');
  container.innerHTML = '';
  if (!entries || entries.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = 'No search history yet.';
    container.appendChild(empty);
    return;
  }
  for (const entry of entries) {
    const row = document.createElement('div');
    row.className = 'pending-row';
    const meta = document.createElement('span');
    meta.className = 'pending-meta';
    const seriesLabel = entry.SeriesTitle || `Series #${entry.ShokoSeriesId}`;
    const episodeLabel = entry.EpisodeTitle || `AniDB ep ${entry.AnidbEpisodeId}`;
    meta.textContent = `${entry.Outcome} · ${seriesLabel} · ${episodeLabel} · ${new Date(entry.TimestampUtc).toLocaleString()}`;
    row.appendChild(meta);
    container.appendChild(row);
  }
}

async function loadHistory() {
  const result = await fetchJson('/Scan/history');
  renderHistory(result.Data);
}

document.getElementById('open-history').onclick = () => {
  document.getElementById('history-panel').classList.toggle('hidden');
  if (!document.getElementById('history-panel').classList.contains('hidden'))
    loadHistory();
};

function renderSuggestions(suggestions) {
  const container = document.getElementById('suggestions-list');
  container.innerHTML = '';
  if (!suggestions || suggestions.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = 'No suggestions right now.';
    container.appendChild(empty);
    return;
  }
  for (const s of suggestions) {
    const row = document.createElement('div');
    row.className = 'suggestion-row';

    const text = document.createElement('div');
    text.innerHTML = `Because you have <strong>${s.OwningSeriesTitle}</strong>, you're missing its <strong>${s.RelationType}</strong>: <em>${s.RelatedTitle}</em>`;
    row.appendChild(text);

    const addBtn = document.createElement('button');
    if (s.RelatedType === 'Movie') {
      addBtn.textContent = 'Add to Radarr';
      addBtn.onclick = () => searchRadarrTitleForSuggestion(s.RelatedTitle, row, addBtn);
    } else {
      addBtn.textContent = 'Add to Sonarr';
      addBtn.onclick = () => searchTitleForSuggestion(s.RelatedTitle, row, addBtn);
    }
    row.appendChild(addBtn);

    container.appendChild(row);
  }
}

function renderCandidateButtons(container, candidates, onPick) {
  for (const candidate of candidates) {
    const btn = document.createElement('button');
    btn.textContent = `${candidate.Title} (${candidate.Year || '?'})`;
    btn.onclick = () => onPick(candidate);
    container.appendChild(btn);
  }
}

async function searchTitleForSuggestion(title, row, triggerBtn) {
  triggerBtn.disabled = true;
  triggerBtn.textContent = 'Searching…';
  const result = await fetchJson('/Sonarr/search-title', { method: 'POST', body: JSON.stringify({ title }) });
  triggerBtn.remove();

  if (!result.Success || !result.Data || result.Data.length === 0) {
    const none = document.createElement('div');
    none.className = 'suggestion-candidates-empty';
    none.textContent = result.Message || 'No Sonarr matches found.';
    row.appendChild(none);
    return;
  }

  const candidates = document.createElement('div');
  candidates.className = 'suggestion-candidates';
  renderCandidateButtons(candidates, result.Data, (candidate) => addDiscoverySeries(candidate.TvdbId, candidate.Title, candidates));
  row.appendChild(candidates);
}

async function findMatchForSeries(series, container, triggerBtn) {
  triggerBtn.disabled = true;
  triggerBtn.textContent = 'Searching…';
  const result = await fetchJson(`/Sonarr/match/${series.ShokoSeriesId}`);
  triggerBtn.remove();

  if (!result.Success || !result.Data.Candidates || result.Data.Candidates.length === 0) {
    const none = document.createElement('span');
    none.className = 'no-match-label';
    none.textContent = 'No Sonarr match found';
    container.appendChild(none);
    return;
  }

  const candidates = document.createElement('div');
  candidates.className = 'suggestion-candidates';
  renderCandidateButtons(candidates, result.Data.Candidates, (candidate) => addAndSearch({ ...series, TvdbId: candidate.TvdbId }));
  container.appendChild(candidates);
}

async function addDiscoverySeries(tvdbId, title, candidatesContainer) {
  const result = await fetchJson('/Sonarr/add-discovery', {
    method: 'POST',
    body: JSON.stringify({ tvdbId, title }),
  });
  alert(result.Success ? `Added ${title}.` : `Failed: ${result.Message}`);
  if (result.Success)
    candidatesContainer.remove();
}

async function searchRadarrTitleForSuggestion(title, row, triggerBtn) {
  triggerBtn.disabled = true;
  triggerBtn.textContent = 'Searching…';
  const result = await fetchJson('/Radarr/search-title', { method: 'POST', body: JSON.stringify({ title }) });
  triggerBtn.remove();

  if (!result.Success || !result.Data || result.Data.length === 0) {
    const none = document.createElement('div');
    none.className = 'suggestion-candidates-empty';
    none.textContent = result.Message || 'No Radarr matches found.';
    row.appendChild(none);
    return;
  }

  const candidates = document.createElement('div');
  candidates.className = 'suggestion-candidates';
  renderCandidateButtons(candidates, result.Data, (candidate) => addRadarrDiscoveryMovie(candidate.TmdbId, candidate.Title, candidates));
  row.appendChild(candidates);
}

async function addRadarrDiscoveryMovie(tmdbId, title, candidatesContainer) {
  const result = await fetchJson('/Radarr/add-discovery', {
    method: 'POST',
    body: JSON.stringify({ tmdbId, title }),
  });
  alert(result.Success ? `Added ${title} to Radarr.` : `Failed: ${result.Message}`);
  if (result.Success)
    candidatesContainer.remove();
}

async function loadSuggestions() {
  const result = await fetchJson('/Scan/related-suggestions');
  renderSuggestions(result.Data);
}

document.getElementById('open-suggestions').onclick = () => {
  document.getElementById('suggestions-panel').classList.toggle('hidden');
  if (!document.getElementById('suggestions-panel').classList.contains('hidden'))
    loadSuggestions();
};

function setStatus(text, ok) {
  const el = document.getElementById('settings-status');
  el.textContent = text;
  el.classList.toggle('ok', ok === true);
  el.classList.toggle('err', ok === false);
}

document.getElementById('test-connection').onclick = async () => {
  const settings = currentSettingsForm();
  const result = await fetchJson('/Settings/test-connection', { method: 'POST', body: JSON.stringify(settings) });
  setStatus(result.Success ? 'Connected.' : `Failed: ${result.Message}`, result.Success);
  if (result.Success)
    await loadSonarrOptions(settings);
};

document.getElementById('radarr-test-connection').onclick = async () => {
  const settings = {
    baseUrl: document.getElementById('radarr-settings-url').value,
    apiKey: document.getElementById('radarr-settings-key').value,
  };
  const result = await fetchJson('/RadarrSettings/test-connection', { method: 'POST', body: JSON.stringify(settings) });
  setStatus(result.Success ? 'Radarr connected.' : `Radarr failed: ${result.Message}`, result.Success);
  if (result.Success)
    await loadRadarrOptions(settings);
};

document.getElementById('save-settings').onclick = async () => {
  const settings = {
    ...currentSettingsForm(),
    qualityProfileId: Number(document.getElementById('settings-quality-profile').value) || null,
    rootFolderPath: document.getElementById('settings-root-folder').value || null,
  };
  await fetchJson('/Settings', { method: 'PUT', body: JSON.stringify(settings) });

  const radarrSettings = {
    baseUrl: document.getElementById('radarr-settings-url').value,
    apiKey: document.getElementById('radarr-settings-key').value,
    qualityProfileId: Number(document.getElementById('radarr-settings-quality-profile').value) || null,
    rootFolderPath: document.getElementById('radarr-settings-root-folder').value || null,
  };
  await fetchJson('/RadarrSettings', { method: 'PUT', body: JSON.stringify(radarrSettings) });
  document.getElementById('radarr-settings-key').value = '';

  setStatus('Saved.', true);
  document.getElementById('settings-key').value = '';
  document.getElementById('settings-webhook').value = '';
  await loadSettings();
};

initTheme();
document.getElementById('live-refresh').checked = localStorage.getItem(LIVE_REFRESH_STORAGE_KEY) === '1';
setLiveRefresh(document.getElementById('live-refresh').checked);
loadSettings();
loadScanResults();
