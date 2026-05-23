document.addEventListener("DOMContentLoaded", function () {
    const selectAllCheckbox = document.getElementById("selectAllItems");
    const productCheckboxes = document.querySelectorAll(".product-select-item");

    // Master checkbox toggle to select/deselect all rows simultaneously
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener("change", function () {
            productCheckboxes.forEach(cb => cb.checked = this.checked);
        });
    }
});

// Enforces configurable business constraints for item comparisons
function executeComparison() {
    const selectedBoxes = document.querySelectorAll(".product-select-item:checked");
    const selectedCount = selectedBoxes.length;

    // Configurable thresholds as requested by specification
    const MIN_COMPARE = 2;
    const MAX_COMPARE = 4;

    if (selectedCount < MIN_COMPARE || selectedCount > MAX_COMPARE) {
        alert(`Comparison Constraint Violation:\nYou must select between ${MIN_COMPARE} and ${MAX_COMPARE} products to proceed.`);
        return;
    }

    let productNames = [];
    selectedBoxes.forEach(cb => {
        productNames.push(cb.getAttribute("data-name"));
    });

    alert(`Launching Comparison Grid Overlay Matrix for:\n${productNames.join(" vs ")}`);
}