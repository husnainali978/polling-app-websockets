// Live Polls demo client.
//
// Talks to the Minimal API (fetch) for CRUD + voting, and to the raw
// WebSocket endpoint (native browser WebSocket API, no libraries) for live
// tally updates. Intentionally framework-free so the WebSocket wiring is
// visible end to end.

(() => {
  const pollListEl = document.getElementById("poll-list");
  const createForm = document.getElementById("create-form");
  const questionInput = document.getElementById("question-input");
  const optionsContainer = document.getElementById("options-container");
  const addOptionBtn = document.getElementById("add-option-btn");
  const createError = document.getElementById("create-error");

  const pollQuestionEl = document.getElementById("poll-question");
  const connectionStatusEl = document.getElementById("connection-status");
  const optionsListEl = document.getElementById("options-list");
  const totalVotesEl = document.getElementById("total-votes");
  const logOutputEl = document.getElementById("log-output");

  const MAX_OPTIONS = 8;

  /** @type {WebSocket | null} */
  let socket = null;
  let currentPollId = null;
  let currentPoll = null;
  let voteInFlight = false;

  // ---------- helpers ----------

  function log(message) {
    const timestamp = new Date().toLocaleTimeString();
    logOutputEl.textContent += `[${timestamp}] ${message}\n`;
    logOutputEl.scrollTop = logOutputEl.scrollHeight;
  }

  function setStatus(state, label) {
    connectionStatusEl.className = `status status-${state}`;
    connectionStatusEl.textContent = label;
  }

  function wsUrlFor(pollId) {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    return `${protocol}//${window.location.host}/ws/polls/${pollId}`;
  }

  function updateUrl(pollId) {
    const url = new URL(window.location.href);
    url.searchParams.set("poll", pollId);
    window.history.replaceState(null, "", url);
  }

  // ---------- poll list ----------

  async function loadPolls() {
    const response = await fetch("/api/polls");
    if (!response.ok) {
      return;
    }
    const polls = await response.json();
    renderPollList(polls);
  }

  function renderPollList(polls) {
    pollListEl.innerHTML = "";

    if (polls.length === 0) {
      const li = document.createElement("li");
      li.className = "empty";
      li.textContent = "No polls yet — create one to get started.";
      pollListEl.appendChild(li);
      return;
    }

    for (const poll of polls) {
      const li = document.createElement("li");
      const button = document.createElement("button");
      button.className = "poll-list-item";
      button.type = "button";
      if (poll.id === currentPollId) {
        button.classList.add("active");
      }
      button.innerHTML = `${escapeHtml(poll.question)}<span class="meta">${poll.optionCount} options · ${poll.totalVotes} votes</span>`;
      button.addEventListener("click", () => selectPoll(poll.id));
      li.appendChild(button);
      pollListEl.appendChild(li);
    }
  }

  function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  }

  // ---------- creating a poll ----------

  addOptionBtn.addEventListener("click", () => {
    const rows = optionsContainer.querySelectorAll(".option-row");
    if (rows.length >= MAX_OPTIONS) {
      return;
    }
    const row = document.createElement("div");
    row.className = "option-row";
    const input = document.createElement("input");
    input.type = "text";
    input.className = "option-input";
    input.maxLength = 200;
    input.placeholder = `Option ${rows.length + 1}`;
    row.appendChild(input);
    optionsContainer.appendChild(row);
  });

  createForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    createError.hidden = true;

    const question = questionInput.value.trim();
    const options = Array.from(optionsContainer.querySelectorAll(".option-input"))
      .map((input) => input.value.trim())
      .filter((value) => value.length > 0);

    if (!question || options.length < 2) {
      createError.textContent = "Enter a question and at least two options.";
      createError.hidden = false;
      return;
    }

    const response = await fetch("/api/polls", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ question, options })
    });

    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      createError.textContent = problem?.errors
        ? Object.values(problem.errors).flat().join(" ")
        : "Could not create the poll.";
      createError.hidden = false;
      return;
    }

    const poll = await response.json();
    createForm.reset();
    // Reset back to two empty option rows.
    optionsContainer.querySelectorAll(".option-row").forEach((row, index) => {
      if (index >= 2) row.remove();
    });

    await loadPolls();
    selectPoll(poll.id);
  });

  // ---------- selecting a poll + WebSocket wiring ----------

  function selectPoll(pollId) {
    if (currentPollId === pollId && socket && socket.readyState === WebSocket.OPEN) {
      return;
    }

    currentPollId = pollId;
    updateUrl(pollId);
    connectSocket(pollId);
    highlightActivePoll();
  }

  function highlightActivePoll() {
    document.querySelectorAll(".poll-list-item").forEach((el) => el.classList.remove("active"));
    loadPolls(); // cheap way to keep the list's vote counts fresh + re-apply highlight
  }

  function connectSocket(pollId) {
    if (socket) {
      socket.close(1000, "switching polls");
      socket = null;
    }

    setStatus("connecting", "connecting…");
    log(`Connecting to ${wsUrlFor(pollId)}`);

    socket = new WebSocket(wsUrlFor(pollId));

    socket.addEventListener("open", () => {
      setStatus("connected", "live");
      log("WebSocket open.");
    });

    socket.addEventListener("message", (event) => {
      try {
        const payload = JSON.parse(event.data);
        log(`Received "${payload.type}" (${payload.poll.totalVotes} total votes).`);
        renderPoll(payload.poll);
      } catch (err) {
        log(`Failed to parse message: ${err}`);
      }
    });

    socket.addEventListener("close", (event) => {
      setStatus("disconnected", "disconnected");
      log(`WebSocket closed (code ${event.code}).`);
    });

    socket.addEventListener("error", () => {
      setStatus("disconnected", "error");
    });
  }

  function renderPoll(poll) {
    currentPoll = poll;
    pollQuestionEl.textContent = poll.question;
    totalVotesEl.textContent = `${poll.totalVotes} total vote${poll.totalVotes === 1 ? "" : "s"}`;

    optionsListEl.innerHTML = "";
    const maxVotes = Math.max(1, ...poll.options.map((o) => o.voteCount));

    for (const option of poll.options) {
      const wrapper = document.createElement("div");
      wrapper.className = "option-tally";

      const top = document.createElement("div");
      top.className = "option-row-top";

      const text = document.createElement("span");
      text.className = "option-text";
      text.textContent = option.text;

      const count = document.createElement("span");
      count.className = "option-count";
      const pct = poll.totalVotes > 0 ? Math.round((option.voteCount / poll.totalVotes) * 100) : 0;
      count.textContent = `${option.voteCount} (${pct}%)`;

      top.appendChild(text);
      top.appendChild(count);

      const track = document.createElement("div");
      track.className = "bar-track";
      const fill = document.createElement("div");
      fill.className = "bar-fill";
      fill.style.width = `${Math.round((option.voteCount / maxVotes) * 100)}%`;
      track.appendChild(fill);

      const voteBtn = document.createElement("button");
      voteBtn.className = "vote-btn";
      voteBtn.type = "button";
      voteBtn.textContent = "Vote";
      voteBtn.disabled = voteInFlight;
      voteBtn.addEventListener("click", () => castVote(option.id));

      wrapper.appendChild(top);
      wrapper.appendChild(track);
      wrapper.appendChild(voteBtn);
      optionsListEl.appendChild(wrapper);
    }
  }

  async function castVote(optionId) {
    if (!currentPollId || voteInFlight) {
      return;
    }
    voteInFlight = true;
    document.querySelectorAll(".vote-btn").forEach((btn) => (btn.disabled = true));

    try {
      const response = await fetch(`/api/polls/${currentPollId}/vote`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ optionId })
      });

      if (!response.ok) {
        log(`Vote failed: HTTP ${response.status}`);
      }
      // No need to render here: the server broadcasts the update over the
      // WebSocket to every subscriber for this poll, including this tab.
    } finally {
      voteInFlight = false;
      document.querySelectorAll(".vote-btn").forEach((btn) => (btn.disabled = false));
    }
  }

  // ---------- boot ----------

  async function init() {
    await loadPolls();

    const params = new URLSearchParams(window.location.search);
    const pollIdFromUrl = params.get("poll");
    if (pollIdFromUrl) {
      selectPoll(pollIdFromUrl);
      return;
    }

    const response = await fetch("/api/polls");
    if (response.ok) {
      const polls = await response.json();
      if (polls.length > 0) {
        selectPoll(polls[0].id);
      }
    }
  }

  init();
})();
