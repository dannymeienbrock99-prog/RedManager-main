(() => {
  "use strict";

  const params = new URLSearchParams(location.search);
  const requestedMode = params.get("mode");
  let mode = requestedMode === "lifetime" || requestedMode === "session"
    ? requestedMode
    : null;
  const forceOffline = params.get("offline") === "1";
  const compact = params.get("compact") === "1";
  const customTitle = params.get("title");
  if (compact) document.body.classList.add("compact");

  const title = document.getElementById("title");
  const summary = document.getElementById("summary");
  const connection = document.getElementById("connection");
  const playersNode = document.getElementById("players");
  const empty = document.getElementById("empty");
  const modeLabel = document.getElementById("modeLabel");
  const toast = document.getElementById("toast");
  const toastName = document.getElementById("toastName");
  const toastText = document.getElementById("toastText");

  let lastEventSequence = 0;
  let lastSessionId = null;
  let previousCounts = new Map();
  let toastTimer = null;

  function setMode(nextMode) {
    mode = nextMode === "lifetime" ? "lifetime" : "session";
    modeLabel.textContent = mode === "lifetime" ? "Tode insgesamt" : "Tode dieser Sitzung";
  }

  setMode(mode || "session");

  function stateLabel(state) {
    switch (state) {
      case "alive": return "lebendig";
      case "downed": return "am Boden";
      case "dead": return "tot";
      case "respawning": return "Respawn";
      default: return "verbunden";
    }
  }

  function countFor(player) {
    return mode === "lifetime" ? player.lifetimeDeaths : player.sessionDeaths;
  }

  function createPlayerRow(player) {
    const row = document.createElement("article");
    row.className = `player ${player.online ? "online" : "offline"} ${player.state || "unknown"}`;
    row.dataset.playerId = player.id;

    const rank = document.createElement("span");
    rank.className = "rank";
    rank.textContent = String(player.rank).padStart(2, "0");

    const main = document.createElement("div");
    main.className = "player-main";
    const name = document.createElement("span");
    name.className = "player-name";
    name.textContent = player.name;
    const meta = document.createElement("span");
    meta.className = "player-meta";
    const onlineText = document.createElement("span");
    const dot = document.createElement("span");
    dot.className = "status-dot";
    onlineText.append(dot, document.createTextNode(player.online ? "online" : "offline"));
    const state = document.createElement("span");
    state.textContent = stateLabel(player.state);
    meta.append(onlineText, state);
    main.append(name, meta);

    const counter = document.createElement("div");
    const countLine = document.createElement("div");
    countLine.className = "deaths";
    const number = document.createElement("span");
    number.className = "death-number";
    number.textContent = String(countFor(player));
    const label = document.createElement("span");
    label.className = "death-label";
    label.textContent = "TODE";
    countLine.append(number, label);
    counter.append(countLine);

    if (mode === "session" && player.lifetimeDeaths > player.sessionDeaths) {
      const lifetime = document.createElement("span");
      lifetime.className = "lifetime";
      lifetime.textContent = `${player.lifetimeDeaths} insgesamt`;
      counter.append(lifetime);
    }

    const oldCount = previousCounts.get(player.id);
    const newCount = countFor(player);
    if (typeof oldCount === "number" && newCount > oldCount) {
      row.classList.add("pulse");
    }
    previousCounts.set(player.id, newCount);

    row.append(rank, main, counter);
    return row;
  }

  function showToast(event) {
    if (!event || event.sequence <= lastEventSequence) return;
    lastEventSequence = event.sequence;
    toastName.textContent = event.playerName;
    toastText.textContent = `ist gestorben – jetzt ${mode === "lifetime" ? event.lifetimeDeaths : event.sessionDeaths} Tode`;
    toast.classList.add("show");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove("show"), 3600);
  }

  function render(data) {
    if (!requestedMode) {
      setMode(data.showLifetimeDeaths ? "lifetime" : "session");
    }

    if (lastSessionId !== data.sessionId) {
      lastSessionId = data.sessionId;
      lastEventSequence = 0;
      previousCounts = new Map();
    }

    connection.classList.remove("offline");
    connection.classList.add("online");
    title.textContent = customTitle || data.title;
    summary.textContent = `${data.onlinePlayers} online · ${data.knownPlayers} automatisch erfasst`;

    const visiblePlayers = (data.players || [])
      .filter(player => player.online || forceOffline || data.showOfflinePlayers)
      .sort((left, right) =>
        countFor(right) - countFor(left) ||
        Number(right.online) - Number(left.online) ||
        right.lifetimeDeaths - left.lifetimeDeaths ||
        left.name.localeCompare(right.name, "de", { sensitivity: "base" }))
      .map((player, index) => ({ ...player, rank: index + 1 }));
    playersNode.replaceChildren(...visiblePlayers.map(createPlayerRow));
    empty.classList.toggle("visible", visiblePlayers.length === 0);
    showToast(data.lastEvent);
  }

  async function refresh() {
    try {
      const response = await fetch(`/api/stats?t=${Date.now()}`, { cache: "no-store" });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      render(await response.json());
    } catch {
      connection.classList.remove("online");
      connection.classList.add("offline");
      summary.textContent = "Keine Verbindung zum Todeszähler";
    }
  }

  refresh();
  setInterval(refresh, 750);
})();
