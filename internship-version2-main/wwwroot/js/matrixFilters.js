// Synchronizes the range slider movement with the manual min/max price text inputs
function updatePriceBoxes(val) {
    const minPriceInput = document.querySelector('input[name="minPrice"]');
    const maxPriceInput = document.querySelector('input[name="maxPrice"]');
    
    // Default sliding behavior sets minimum anchor at 0, moving the max ceiling values
    minPriceInput.value = 0;
    maxPriceInput.value = val;
}

// Automatically triggers form submission when rating filters are toggled
document.addEventListener("DOMContentLoaded", function () {
    const ratingRadios = document.querySelectorAll('input[name="minRating"]');
    ratingRadios.forEach(radio => {
        radio.addEventListener('change', () => {
            document.getElementById('filterMatrixForm').submit();
        });
    });
});