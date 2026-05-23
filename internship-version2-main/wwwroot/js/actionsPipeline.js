function triggerExport() {
    // Collect current filtering state variables to apply conditional export matching your grid view
    const urlParams = new URLSearchParams(window.location.search);
    window.location.href = '/Product/ExportData?' + urlParams.toString();
}

function triggerImport() {
    // Dynamically generates a hidden file input element to handle instant template uploads safely
    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.accept = '.xlsx, .xls';
    
    fileInput.onchange = e => {
        const file = e.target.files[0];
        const formData = new FormData();
        formData.append('alexaExcelFile', file);

        fetch('/Product/ImportData', {
            method: 'POST',
            body: formData
        }).then(response => {
            if (response.ok) {
                alert("Import successful! Refreshing product table data grid...");
                window.location.reload();
            } else {
                alert("Error detected during file parsing validation routines.");
            }
        });
    };
    
    fileInput.click();
}