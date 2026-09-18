// SSH Keys page: generic copy-to-clipboard for any element carrying a `.copy-btn` with a
// `data-copy-target` id — used by the one-time enrollment token and its curl example, which only
// exist once htmx swaps `_SshEnrollmentToken.cshtml` into #result. Plain vanilla JS, no framework,
// matching execution-log.js's copy button.
(function () {
    "use strict";

    function wireCopyButtons(root) {
        root.querySelectorAll(".copy-btn").forEach(function (button) {
            if (button.dataset.copyWired) {
                return;
            }
            button.dataset.copyWired = "true";

            button.addEventListener("click", function () {
                var target = document.getElementById(button.getAttribute("data-copy-target"));
                if (!target || !navigator.clipboard) {
                    return;
                }

                navigator.clipboard.writeText(target.textContent || "").then(function () {
                    var original = button.textContent;
                    button.textContent = "Copied!";
                    setTimeout(function () {
                        button.textContent = original;
                    }, 1200);
                });
            });
        });
    }

    wireCopyButtons(document);

    document.body.addEventListener("htmx:afterSwap", function (event) {
        if (event.target) {
            wireCopyButtons(event.target);
        }
    });
})();
