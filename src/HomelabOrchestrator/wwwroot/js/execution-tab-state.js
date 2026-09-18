// Execution detail page: preserves manually-expanded <details> rows (Tasks/Hosts tabs) across the
// live poll that replaces #tasks-panel/#hosts-panel wholesale via hx-swap="outerHTML" — without
// this, every poll would silently re-collapse anything the operator just opened, since the fresh
// server render only auto-opens a row that's currently failing. Plain vanilla JS, no framework,
// matching runner.js's conventions; this only remembers open/closed per row, nothing that reaches
// the server.
(function () {
    "use strict";

    var openState = Object.create(null);

    function stateKey(details) {
        var taskName = details.getAttribute("data-task-name");
        if (taskName !== null) {
            return "task:" + taskName;
        }

        var hostname = details.getAttribute("data-hostname");
        if (hostname !== null) {
            return "host:" + hostname;
        }

        return null;
    }

    // "toggle" doesn't bubble in every browser, but a capture-phase listener on document still
    // fires for it regardless — capture always walks down to the target, independent of bubbling.
    document.addEventListener("toggle", function (event) {
        var details = event.target;
        if (!details || details.tagName !== "DETAILS") {
            return;
        }

        var key = stateKey(details);
        if (key) {
            openState[key] = details.open;
        }
    }, true);

    function restoreOpenState() {
        document.querySelectorAll("details[data-task-name], details[data-hostname]").forEach(function (details) {
            var key = stateKey(details);
            if (key && Object.prototype.hasOwnProperty.call(openState, key)) {
                details.open = openState[key];
            }
        });
    }

    document.body.addEventListener("htmx:afterSwap", restoreOpenState);
})();
