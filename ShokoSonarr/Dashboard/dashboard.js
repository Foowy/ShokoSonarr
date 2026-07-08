const API_BASE = window.location.pathname.replace(/\/dashboard.*$/, '').replace('/api/plugin/ShokoSonarr', '/api/v1.0/ShokoSonarr');

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

function renderSeries(snapshot) {
  const container = document.getElementById('series-list');
  container.innerHTML = '';
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

    const header = document.createElement('div');
    header.className = 'header';

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
      const label = document.createElement('span');
      label.className = 'no-match-label';
      label.textContent = 'No Sonarr match available';
      rowActions.appendChild(label);
    }

    const specialsToggle = document.createElement('div');
    specialsToggle.className = 'specials-toggle';
    const currentOverride = series.IncludeSpecialsOverride === null || series.IncludeSpecialsOverride === undefined
      ? 'default' : String(series.IncludeSpecialsOverride);
    for (const [key, label, value] of [['default', 'Default', null], ['true', 'Include', true], ['false', 'Exclude', false]]) {
      const btn = document.createElement('button');
      btn.textContent = label;
      if (key === currentOverride) btn.classList.add('active');
      btn.onclick = (e) => { e.stopPropagation(); setSpecialsOverride(series.ShokoSeriesId, value); };
      specialsToggle.appendChild(btn);
    }
    rowActions.appendChild(specialsToggle);
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

let savedQualityProfileId = null;
let savedRootFolderPath = null;

function currentSettingsForm() {
  return {
    baseUrl: document.getElementById('settings-url').value,
    apiKey: document.getElementById('settings-key').value,
    scanIntervalHours: Number(document.getElementById('settings-interval').value),
    includeSpecials: document.getElementById('settings-include-specials').checked,
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

async function loadSettings() {
  const result = await fetchJson('/Settings');
  document.getElementById('settings-url').value = result.Data.BaseUrl || '';
  document.getElementById('settings-interval').value = result.Data.ScanIntervalHours;
  document.getElementById('settings-include-specials').checked = result.Data.IncludeSpecials;
  savedQualityProfileId = result.Data.QualityProfileId;
  savedRootFolderPath = result.Data.RootFolderPath;
  populateSelect('settings-quality-profile', savedQualityProfileId ? [{ Id: savedQualityProfileId, Name: `#${savedQualityProfileId}` }] : [], 'Id', 'Name', savedQualityProfileId);
  populateSelect('settings-root-folder', savedRootFolderPath ? [{ Path: savedRootFolderPath }] : [], 'Path', 'Path', savedRootFolderPath);
  // The stored API key is masked here, not usable to call Sonarr — dropdowns above show the saved
  // value as a single placeholder option; Test Connection (re-entering the real key) repopulates
  // them with the live list from Sonarr.
}

document.getElementById('scan-now').onclick = async () => {
  const result = await fetchJson('/Scan', { method: 'POST' });
  renderSeries(result);
};

document.getElementById('open-settings').onclick = () => {
  document.getElementById('settings-panel').classList.toggle('hidden');
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

document.getElementById('save-settings').onclick = async () => {
  const settings = {
    ...currentSettingsForm(),
    qualityProfileId: Number(document.getElementById('settings-quality-profile').value) || null,
    rootFolderPath: document.getElementById('settings-root-folder').value || null,
  };
  await fetchJson('/Settings', { method: 'PUT', body: JSON.stringify(settings) });
  setStatus('Saved.', true);
};

loadSettings();
loadScanResults();
