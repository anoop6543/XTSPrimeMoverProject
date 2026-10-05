// HTML overlay: mode badge, KPI chips, AI findings, autopilot decision and cinematic captions.
const $ = (id) => document.getElementById(id);

export class Hud {
  constructor() {
    this.cache = {};
    this.el = {
      mode: $('mode-badge'), time: $('sim-time'), oee: $('kpi-oee'), tput: $('kpi-tput'), yield: $('kpi-yield'),
      kw: $('kpi-kw'), wh: $('kpi-wh'), wip: $('kpi-wip'), ap: $('kpi-ap'), apBox: $('kpi-autopilot'),
      alerts: $('alerts'), decision: $('decision'), caption: $('caption'), loading: $('loading')
    };
  }

  set(key, el, value) {
    if (this.cache[key] === value) return;
    this.cache[key] = value;
    el.textContent = value;
  }

  setMode(mode) {
    this.set('mode', this.el.mode, mode);
    this.el.mode.className = 'badge ' + mode.toLowerCase();
  }

  hideLoading() { this.el.loading.classList.add('hidden'); }

  update(frame) {
    if (!frame) return;
    const k = frame.kpi ?? {};
    this.set('time', this.el.time, frame.t.toFixed(1));
    this.set('oee', this.el.oee, `${Math.round((k.oee ?? 0) * 100)} %`);
    this.set('tput', this.el.tput, `${(k.tput ?? 0).toFixed(2)}/min`);
    this.set('yield', this.el.yield, `${((k.yield ?? 1) * 100).toFixed(1)} %`);
    this.set('kw', this.el.kw, `${(k.kw ?? 0).toFixed(2)} kW`);
    this.set('wh', this.el.wh, k.whPart ? `${Math.round(k.whPart)} Wh/mod` : '–');
    this.set('wip', this.el.wip, `${k.wip ?? 0} / ${k.wipCap ?? '–'}`);
    this.set('ap', this.el.ap, k.autopilot ? 'ENGAGED' : 'manual');
    this.el.apBox.classList.toggle('on', !!k.autopilot);

    const alertsHtml = (frame.alerts ?? []).map((a) => `<li class="${a.sev}"><span class="sev">${a.sev}</span>${escapeHtml(a.title)}</li>`).join('')
      || '<li class="Info"><span class="sev">ok</span>No findings – line running within its fingerprint.</li>';
    if (this.cache.alerts !== alertsHtml) {
      this.el.alerts.innerHTML = alertsHtml;
      this.cache.alerts = alertsHtml;
    }

    const decision = frame.decisions?.[0];
    const decisionHtml = decision ? `<b>Autopilot:</b> ${escapeHtml(decision)}` : (frame.faults?.length ? `<b>What-if active:</b> ${escapeHtml(frame.faults[0])}` : '');
    if (this.cache.decision !== decisionHtml) {
      this.el.decision.innerHTML = decisionHtml;
      this.cache.decision = decisionHtml;
    }
  }

  caption(c) {
    const html = c ? `${escapeHtml(c.title)}${c.sub ? `<small>${escapeHtml(c.sub)}</small>` : ''}` : '';
    if (this.cache.caption !== html) {
      this.el.caption.innerHTML = html;
      this.cache.caption = html;
    }
    this.el.caption.classList.toggle('show', !!c);
  }
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (ch) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
}
