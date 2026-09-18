// Execution detail page: the raw-output console's client-only widget state — sticky auto-scroll,
// search filtering, wrap toggle, and copy. Plain vanilla JS, no framework, matching runner.js.
// New lines arrive via htmx (see _ExecutionLogTail.cshtml's self-perpetuating poll trigger); this
// script never fetches anything itself, it only reacts to what htmx already swapped in.
(function () {
    "use strict";

    var console_ = document.getElementById("log-console");
    if (!console_) {
        return;
    }

    var searchInput = document.getElementById("log-search");
    var wrapToggle = document.getElementById("log-wrap");
    var copyBtn = document.getElementById("log-copy");

    var stickToBottom = true;

    function isNearBottom() {
        return console_.scrollHeight - console_.scrollTop - console_.clientHeight < 40;
    }

    console_.addEventListener("scroll", function () {
        stickToBottom = isNearBottom();
    });

    document.body.addEventListener("htmx:afterSwap", function (event) {
        if (!event.target || event.target.id !== "log-poll") {
            return;
        }

        if (stickToBottom) {
            console_.scrollTop = console_.scrollHeight;
        }
    });

    if (searchInput) {
        searchInput.addEventListener("input", function () {
            var query = searchInput.value.trim().toLowerCase();
            console_.querySelectorAll(".log-line").forEach(function (line) {
                var text = line.textContent.toLowerCase();
                line.classList.toggle("log-line-hidden", query.length > 0 && text.indexOf(query) === -1);
            });
        });
    }

    if (wrapToggle) {
        wrapToggle.addEventListener("change", function () {
            console_.classList.toggle("log-console-wrap", wrapToggle.checked);
        });
    }

    if (copyBtn && navigator.clipboard) {
        copyBtn.addEventListener("click", function () {
            navigator.clipboard.writeText(console_.innerText).then(function () {
                var original = copyBtn.textContent;
                copyBtn.textContent = "Copied!";
                setTimeout(function () {
                    copyBtn.textContent = original;
                }, 1200);
            });
        });
    }
})();
