// Runner page: checkbox-based container multi-select and the playbook picker modal. Plain
// vanilla JS, no framework — matches this app's "HTMX, not a JS framework" architectural
// decision; this only drives client-side widget state, every value still round-trips through
// RunFormModel.Target/PlaybookName exactly as before.
(function () {
    "use strict";

    var form = document.getElementById("runner-form");
    if (!form) {
        return;
    }

    var targetInput = document.getElementById("target-input");
    var playbookNameInput = document.getElementById("playbook-name-input");
    var runSubmit = document.getElementById("run-submit");
    var selectedCount = document.getElementById("selected-count");
    var containerList = document.getElementById("container-list");
    var tagFilter = document.getElementById("tag-filter");
    var selectTaggedBtn = document.getElementById("select-tagged");
    var selectAllBtn = document.getElementById("select-all-running");
    var clearBtn = document.getElementById("clear-selection");

    // null (nothing chosen yet) | "all" | "tag:<tag>" | "selection" — "all"/"tag:<tag>" are live,
    // re-evaluated by the worker at run time; checking/unchecking any individual box locks the
    // target into "selection" (a frozen hostname list) from then on.
    var mode = null;

    function checkboxes() {
        return containerList ? Array.prototype.slice.call(containerList.querySelectorAll(".container-checkbox")) : [];
    }

    function rowFor(checkbox) {
        return checkbox.closest(".container-row");
    }

    function escapeHtml(value) {
        var div = document.createElement("div");
        div.textContent = value;
        return div.innerHTML;
    }

    function updateSubmitState() {
        if (!runSubmit) {
            return;
        }

        var hasPlaybook = !!(playbookNameInput && playbookNameInput.value);
        var hasTarget = !!(targetInput && targetInput.value);
        runSubmit.disabled = !(hasPlaybook && hasTarget);
    }

    function updateTargetState() {
        var checked = checkboxes().filter(function (cb) {
            return cb.checked;
        });

        checkboxes().forEach(function (cb) {
            rowFor(cb).classList.toggle("selected", cb.checked);
        });

        if (selectedCount) {
            selectedCount.textContent = checked.length + " selected";
        }

        if (targetInput) {
            if (mode === "all") {
                targetInput.value = "all";
            } else if (mode && mode.indexOf("tag:") === 0) {
                targetInput.value = mode;
            } else if (checked.length > 0) {
                targetInput.value = "selection:" + checked.map(function (cb) {
                    return cb.value;
                }).join(",");
            } else {
                targetInput.value = "";
            }
        }

        updateSubmitState();
    }

    if (containerList) {
        containerList.addEventListener("change", function (event) {
            if (!event.target.classList.contains("container-checkbox")) {
                return;
            }

            mode = "selection";
            updateTargetState();
        });
    }

    if (selectAllBtn) {
        selectAllBtn.addEventListener("click", function () {
            mode = "all";
            checkboxes().forEach(function (cb) {
                cb.checked = true;
            });
            updateTargetState();
        });
    }

    if (selectTaggedBtn && tagFilter) {
        selectTaggedBtn.addEventListener("click", function () {
            var tag = tagFilter.value;
            if (!tag) {
                return;
            }

            mode = "tag:" + tag;
            checkboxes().forEach(function (cb) {
                var tags = (rowFor(cb).dataset.tags || "").split(",");
                cb.checked = tags.indexOf(tag) !== -1;
            });
            updateTargetState();
        });
    }

    if (clearBtn) {
        clearBtn.addEventListener("click", function () {
            mode = null;
            checkboxes().forEach(function (cb) {
                cb.checked = false;
            });
            updateTargetState();
        });
    }

    if (tagFilter) {
        tagFilter.addEventListener("change", function () {
            var tag = tagFilter.value;

            if (containerList) {
                containerList.querySelectorAll(".container-row").forEach(function (row) {
                    var tags = (row.dataset.tags || "").split(",");
                    row.classList.toggle("hidden", !!tag && tags.indexOf(tag) === -1);
                });
            }

            if (selectTaggedBtn) {
                selectTaggedBtn.disabled = !tag;
            }
        });
    }

    // Playbook picker modal
    var modal = document.getElementById("playbook-modal");
    var openModalBtn = document.getElementById("open-playbook-modal");
    var closeModalBtn = document.getElementById("close-playbook-modal");
    var cancelModalBtn = document.getElementById("cancel-playbook-modal");
    var useBtn = document.getElementById("use-playbook");
    var searchInput = document.getElementById("playbook-search");
    var playbookRows = modal ? Array.prototype.slice.call(modal.querySelectorAll(".playbook-row")) : [];
    var detailPane = document.getElementById("playbook-detail");
    var summaryPane = document.getElementById("playbook-summary");

    var highlighted = null;

    function renderDetail(row) {
        if (!detailPane) {
            return;
        }

        if (!row) {
            detailPane.innerHTML = '<p class="muted">Select a playbook from the list.</p>';
            return;
        }

        var name = row.dataset.name || "";
        var description = row.dataset.description || "";
        var steps = (row.dataset.steps || "").split("|").filter(function (s) {
            return s.length > 0;
        });

        var html = "<h3>" + escapeHtml(name) + "</h3>";
        html += '<p class="muted">' + escapeHtml(description) + "</p>";
        if (steps.length > 0) {
            html += '<div class="playbook-detail-steps-label">Steps (' + steps.length + ")</div>";
            html += "<ol>" + steps.map(function (s) {
                return "<li>" + escapeHtml(s) + "</li>";
            }).join("") + "</ol>";
        }

        detailPane.innerHTML = html;
    }

    playbookRows.forEach(function (row) {
        row.addEventListener("click", function () {
            playbookRows.forEach(function (r) {
                r.classList.remove("selected");
            });
            row.classList.add("selected");
            highlighted = row;
            renderDetail(row);
            if (useBtn) {
                useBtn.disabled = false;
            }
        });
    });

    if (searchInput) {
        searchInput.addEventListener("input", function () {
            var query = searchInput.value.trim().toLowerCase();
            playbookRows.forEach(function (row) {
                var name = (row.dataset.name || "").toLowerCase();
                row.classList.toggle("hidden", query.length > 0 && name.indexOf(query) === -1);
            });
        });
    }

    if (openModalBtn && modal && typeof modal.showModal === "function") {
        openModalBtn.addEventListener("click", function () {
            modal.showModal();
        });
    }

    function closeModal() {
        if (modal && modal.open) {
            modal.close();
        }
    }

    if (closeModalBtn) {
        closeModalBtn.addEventListener("click", closeModal);
    }

    if (cancelModalBtn) {
        cancelModalBtn.addEventListener("click", closeModal);
    }

    if (modal) {
        modal.addEventListener("click", function (event) {
            if (event.target === modal) {
                closeModal();
            }
        });
    }

    if (useBtn) {
        useBtn.addEventListener("click", function () {
            if (!highlighted) {
                return;
            }

            var name = highlighted.dataset.name || "";
            var description = highlighted.dataset.description || "";

            if (playbookNameInput) {
                playbookNameInput.value = name;
            }

            if (summaryPane) {
                summaryPane.innerHTML =
                    '<div class="playbook-summary-name">' + escapeHtml(name) + "</div>" +
                    '<p class="muted playbook-summary-desc">' + escapeHtml(description) + "</p>";
            }

            updateSubmitState();
            closeModal();
        });
    }

    updateTargetState();
})();
