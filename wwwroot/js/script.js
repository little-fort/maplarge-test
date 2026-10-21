// Folder sizes load separately from the listing. Tracked so that it can be cancelled
// when the user leaves the folder
let folderSizesRequest = null;
const sizePlaceholder = '<span class="size-pending" title="Calculating size…">…</span>';

// The listing being loaded. Starting another cancels it
let directoryRequest = null;

// The folder whose listing is on screen. Actions apply to it rather than to the address bar,
// which changes as soon as the user navigates
let shownPath = null;

// The search being run. Starting another, or opening a folder, cancels it
let searchRequest = null;

// True while search results are shown (or on their way) in place of the folder's listing
let searchActive = false;

// A search result to highlight once its folder's listing arrives
let pendingHighlight = null;

$(document).ready(function () {
    const path = getPath();
    if (!path || path.length === 0 || path.includes(':') || path.includes(".."))
        updateQueryParam('path', '/', false);

    normalizePath();
    getDirectory();
    showClipboard();

    // Ensure back/forward clicks update displayed folder
    window.addEventListener('popstate', (event) => {
        hideBanner();
        getDirectory();
    });

    $('#banner-close').on('click', hideBanner);

    // Folder/file click handler
    $('#table-data').on('click', 'a[data-name]', function () {
        if (this.dataset.type === 'folder')
            navigate('down', this.dataset.name);
        else if (this.dataset.type === 'file')
            setFileProperties(this.dataset.path, this);
    });

    // Selection checkboxes
    $('#table-data').on('change', 'input.row-check', updateSelection);
    $('#select-all').on('change', function () {
        $('#table-data input.row-check').prop('checked', this.checked);
        updateSelection();
    });

    // Toolbar and clipboard
    $('#action-copy').on('click', () => addSelectionToClipboard('COPY'));
    $('#action-cut').on('click', () => addSelectionToClipboard('CUT'));
    $('#action-paste').on('click', pasteClipboard);
    $('#action-rename').on('click', renameSelection);
    $('#action-delete').on('click', deleteSelection);
    $('#action-download').on('click', downloadSelection);
    $('#action-upload').on('click', () => $('#upload-input').trigger('click'));
    $('#upload-input').on('change', function () {
        uploadFiles(Array.from(this.files));
        this.value = ''; // so choosing the same files again still counts as a change
    });
    $('#clipboard-list').on('click', 'button[data-index]', function () {
        removeClipboardItem(Number(this.dataset.index));
    });
    $('#clipboard-clear').on('click', () => saveClipboard([]));

    // Search
    $('#search-form').on('submit', function (event) {
        event.preventDefault(); // searches in place instead of reloading the page
        runSearch($('#search-box').val());
    });
    $('#search-box').on('keydown', function (event) {
        if (event.key === 'Escape' && searchActive) {
            event.preventDefault();
            clearSearch();
        }
    });
    // Emptying the box, including with its own clear button, ends the search
    $('#search-box').on('input', function () {
        if (searchActive && this.value.trim() === '')
            clearSearch();
    });
    $('#search-clear').on('click', clearSearch);
    $('#table-data').on('click', 'a[data-result-path]', function () {
        openSearchResult(this.dataset.resultPath, this.dataset.type);
    });
});

function getPath() {
    const params = new URLSearchParams(window.location.search);
    let path = params.get('path');

    return path;
}

function normalizePath() {
    let path = getPath();
    path = path.replaceAll('\\', '/');
    updateQueryParam('path', path);
}

function updateQueryParam(key, value, allowBack) {
    const url = new URL(window.location.href);
    url.searchParams.set(key, value);

    if (allowBack === true)
        window.history.pushState({}, '', url);
    else
        window.history.replaceState({}, '', url);
}

function getDirectory() {
    const path = getPath();
    endSearch(); // the folder's listing takes the place of any search results
    folderSizesRequest?.abort(); // the sizes are for the folder being left
    directoryRequest?.abort();
    directoryRequest = $.ajax({
        url: '/directory',
        type: 'GET',
        data: { path: path },
        success: function (data) {
            console.log("Success: ", data);
            localStorage.setItem(getPath(), JSON.stringify(data));
            shownPath = data.path;
            showLocation(path);
            setProperties(data.directory);
            // A search started while this was loading keeps the table
            if (!searchActive) {
                showContents(data.contents);
                highlightPendingResult(); // after the folder's properties, so an opened file's replace them
            }
            getFolderSizes(path);
        },
        error: function (error, status) {
            if (status === 'abort')
                return; // replaced by a newer request
            console.error("Error: ", error);
            const problem = error.status === 404 ? "was not found on the server" : "could not be loaded";

            // Fall back to the top-level folder once to avoid looping.
            if (path !== '/') {
                showBanner(`The path "${path}" ${problem}.`);
                updateQueryParam('path', '/', false);
                getDirectory();
            }
            else
                showBanner(`The top-level folder ${problem}. Try reloading the page.`);
        }
    });
}

function getFolderSizes(path) {
    folderSizesRequest?.abort(); // only the listing on screen needs sizes
    folderSizesRequest = $.ajax({
        url: '/directory/sizes',
        type: 'GET',
        data: { path: path },
        success: function (data) {
            showFolderSizes(data);
        },
        error: function (error, status) {
            if (status === 'abort')
                return; // cancelled on purpose; a newer request has taken over
            console.error("Error loading folder sizes: ", error);
            showFolderSizes(null);
        }
    });
}

function showFolderSizes(data) {
    const sizes = new Map((data ? data.folders : []).map(folder => [folder.name, folder]));

    $('#table-data input.row-check[data-type="folder"]').each(function () {
        const folder = sizes.get(this.dataset.name);
        const cell = $(this).closest('tr').find('td.col-size');
        if (folder && folder.size !== null)
            cell.text(folder.displaySize);
        else
            cell.text('—').attr('title', 'Size not available');
    });

    $('#properties-size').text(data ? data.displaySize : 'Not available');
}

function navigate(direction, name) {
    hideBanner();
    const path = getPath();
    let dirs = path.split('/');

    if (direction === 'up')
        dirs.pop();
    else if (direction === 'down') {
        if (path === '/')
            dirs = ['', name];
        else
            dirs.push(name);
    }

    if (dirs.length > 1)
        updateQueryParam('path', dirs.join('/'), true);
    else
        updateQueryParam('path', '/', true);

    getDirectory();
}

function showLocation(path) {
    $('#location-path').text(path);
}

// Shows a message above the listing
function showBanner(message, kind = 'error', items = []) {
    $('#banner').toggleClass('banner-success', kind === 'success').prop('hidden', false);
    $('#banner-message').text(message);
    $('#banner-list')
        .html(items.map(item => `<li>${htmlEncode(item)}</li>`).join(''))
        .prop('hidden', items.length === 0);
}

function hideBanner() {
    $('#banner').prop('hidden', true);
}

function showContents(dir) {
    let tableRows = '';
    const path = getPath();

    if (path !== '/') {
        tableRows += `
            <tr>
                <td class="col-select"></td>
                <td class="col-icon">📁</td>
                <td class="col-name"><a onclick="navigate('up');">.. (parent directory)</a></td>
                <td class="col-type"></td>
                <td class="col-modified"></td>
                <td class="col-size"></td>
            </tr>
        `;
    }

    $.each(dir, function (index, object) {
        const objectIcon = object.isLink ? '🔗' : object.type === 0 ? '📁' : getFileTypeIcon(object.extension);
        const objectType = object.type === 0 ? 'folder' : 'file';
        const name = htmlEncode(object.name);
        // The server doesn't open links, so a linked folder isn't clickable
        const navLink = object.isLink && object.type === 0
            ? `<span class="link-name" title="Links aren't opened, so browsing stays inside this folder.">${name}</span>`
            : `<a data-type="${objectType}" data-name="${name}" data-path="${htmlEncode(object.fullPath)}">${name}</a>`;
        const checkbox = `<input type="checkbox" class="row-check" aria-label="Select ${name}" data-name="${name}" data-type="${objectType}" data-extension="${htmlEncode(object.extension)}" data-link="${object.isLink}">`;
        const localDate = (new Date(object.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
        const objectSize = object.type === 0 ? sizePlaceholder : object.displaySize;
        tableRows += `
            <tr>
                <td class="col-select"><label class="select-cell">${checkbox}</label></td>
                <td class="col-icon">${objectIcon}</td>
                <td class="col-name">${navLink}</td>
                <td class="col-type">${htmlEncode(object.extension)}</td>
                <td class="col-modified">${localDate}</td>
                <td class="col-size">${objectSize}</td>
            </tr>
        `;
    })

    $('#table-data tbody').html(tableRows);
    updateSelection();
}

function setFileProperties(fullPath, link) {
    const data = localStorage.getItem(shownPath);
    if (data) {
        console.log("Retrieving properties for: " + fullPath);
        const directory = JSON.parse(data);
        const file = directory.contents.find(object => object.fullPath === fullPath);
        setProperties(file);
        // Highlight the row whose details are now showing
        $(link).closest('tr').addClass('selected').siblings().removeClass('selected');
    }
    else
        console.error("Object cache deleted or expired. Reload page to refresh file details.");
}

function setProperties(item) {
    const icon = getFileTypeIcon(item.extension);
    $("#properties-icon").html(icon);
    $("#properties-name").text(item.name);

    const localDate = (new Date(item.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
    const itemType = item.type === 0 ? "Folder" : htmlEncode(item.extension);
    let tableRows = `
        <tr>
            <td>Type</td>
            <td>${itemType}</td>
        </tr>
    `;

    if (item.type === 0) {
        tableRows += `
            <tr>
                <td>Size</td>
                <td id="properties-size"><span class="size-pending">Calculating…</span></td>
            </tr>
            <tr>
                <td>Items</td>
                <td>${item.countDisplay}</td>
            </tr>
        `;
    }
    if (item.type === 1) {
        tableRows += `
            <tr>
                <td>Size</td>
                <td>${item.displaySize}</td>
            </tr>
            <tr>
                <td>Last Modified</td>
                <td>${localDate}</td>
            </tr>
        `;
    }

    $('#properties-data').html(tableRows);
}

function getFileTypeIcon(extension) {
    if (extension === "") return "📁";
    if (extension === "FILE") return "❔📝";

    const exe = ["EXE"]
    const cab = ["CAB", "MSI", "MSIX", "GZ"];
    const zip = ["ZIP", "RAR", "7Z", "TAR"];
    const text = ["TXT", "MD", "CSV", "LOG", "INI", "JSON", "XML"];
    const doc = ["DOC", "DOCX", "ODT", "RTF", "PDF"];
    const code = ["CS", "CPP", "H", "JAVA", "RB", "PHP"];
    const video = ["MP4", "MOV", "MPEG", "AVI", "MKV", "WEBM"];
    const audio = ["MP3", "WAV", "AAC", "OGG"];
    const image = ["JPEG", "JPG", "PNG", "TIFF", "BMP", "GIF"];
    const web = ["HTM", "HTML", "CSS"];
    const script = ["JS", "PS1", "PY", "SH", "BAT"];
    const vm = ["VMDK", "VHD", "VHDX", "RDP"];

    if (exe.includes(extension)) return "💽";
    if (cab.includes(extension)) return "🗄️";
    if (zip.includes(extension)) return "📦";
    if (text.includes(extension)) return "📝";
    if (doc.includes(extension)) return "📑";
    if (code.includes(extension)) return "📃";
    if (video.includes(extension)) return "🎬";
    if (audio.includes(extension)) return "🔉";
    if (image.includes(extension)) return "🖼️";
    if (web.includes(extension)) return "🌐";
    if (script.includes(extension)) return "📜";
    if (vm.includes(extension)) return "🖥️";

    return "📄";
}

function htmlEncode(str) {
    const entityMap = {
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': '&quot;',
        "'": '&#39;',
        "/": '&#x2F;'
    };

    const retval = String(str).replace(/[&<>"'\/]/g, function (s) {
        return entityMap[s];
    });

    return retval;
}

// Request errors

// Used when an error response doesn't carry a message worth showing
const httpErrorMessages = {
    400: "The request wasn't valid.",
    403: "That action isn't allowed.",
    404: "The item couldn't be found. It may have been moved or deleted.",
    409: "An item with that name already exists.",
    500: "Something went wrong on the server.",
};

// Returns the body of a failed request: an object for JSON, a string for anything else,
// or null when there's no body.
function getErrorBody(xhr) {
    if (xhr.responseJSON !== undefined)
        return xhr.responseJSON;
    if (!xhr.responseText)
        return null;

    try {
        return JSON.parse(xhr.responseText);
    }
    catch {
        return xhr.responseText;
    }
}

// Builds a message for the user from a failed request
function getErrorMessage(xhr) {
    if (xhr.status === 0)
        return "The server couldn't be reached. Check your connection and try again.";

    const body = getErrorBody(xhr);
    if (body && typeof body === 'object') {
        const validationMessages = body.errors ? Object.values(body.errors).flat() : [];
        if (validationMessages.length > 0)
            return validationMessages.join(' ');
        if (body.detail)
            return body.detail;
    }
    else if (typeof body === 'string') {
        // A short single line is a message; anything longer is a stack trace or an HTML page
        const text = body.trim();
        if (text && text.length <= 300 && !/[\r\n]/.test(text) && !text.startsWith('<'))
            return text;
    }

    return httpErrorMessages[xhr.status] ?? body?.title ?? `The request failed (HTTP ${xhr.status}).`;
}

// Selection

// The checked rows, in listing order
function getSelectedItems() {
    return $('#table-data input.row-check:checked').map(function () {
        return {
            name: this.dataset.name,
            type: this.dataset.type,
            extension: this.dataset.extension,
            isLink: this.dataset.link === 'true'
        };
    }).get();
}

// Keeps the select-all box, row highlights, count and toolbar in step with the checkboxes
function updateSelection() {
    const boxes = $('#table-data input.row-check');
    const checked = boxes.filter(':checked').length;

    boxes.each(function () {
        $(this).closest('tr').toggleClass('is-checked', this.checked);
    });
    $('#select-all').prop({
        checked: boxes.length > 0 && checked === boxes.length,
        indeterminate: checked > 0 && checked < boxes.length,
        disabled: boxes.length === 0
    });
    $('#selection-count').text(checked > 0 ? `${checked} selected` : '');
    updateToolbar();
}

function clearSelection() {
    $('#table-data input.row-check').prop('checked', false);
    updateSelection();
}

// True while an action request is running, so the toolbar can't send it twice
let actionRunning = false;

function setActionRunning(running) {
    actionRunning = running;
    updateToolbar();
}

function updateToolbar() {
    const nothingSelected = $('#table-data input.row-check:checked').length === 0;
    // Disable actions while search results populate
    const locked = actionRunning || searchActive;
    $('#action-copy, #action-cut, #action-rename, #action-delete').prop('disabled', locked || nothingSelected);
    $('#action-paste').prop('disabled', locked || shownPath === null || getClipboard().length === 0);
    $('#action-download').prop('disabled', searchActive || nothingSelected);
    $('#action-upload').prop('disabled', locked || shownPath === null);
}

// Browser path of an item in the folder being shown
function childPath(name) {
    return (shownPath === '/' ? '' : shownPath) + '/' + name;
}

function describeItems(items) {
    return items.length === 1 ? `"${items[0].name}"` : `${items.length} items`;
}

// Shows what an action did. Failed items are listed with the server's reason for each.
function showActionResult(result, verb) {
    if (result.failed.length === 0) {
        showBanner(`${result.message}.`, 'success');
        return;
    }

    const count = result.failed.length;
    const summary = `${count} ${count === 1 ? "item couldn't" : "items couldn't"} be ${verb}:`;
    showBanner(result.message ? `${result.message}. ${summary}` : summary, 'error',
        result.failed.map(failure => `${failure.name}: ${failure.reason}`));
}

// Clipboard

function getClipboard() {
    const clipboardString = localStorage.getItem("clipboard");
    const clipboard = clipboardString ? JSON.parse(clipboardString) : [];
    return clipboard;
}

function saveClipboard(clipboard) {
    if (clipboard.length > 0)
        localStorage.setItem("clipboard", JSON.stringify(clipboard));
    else
        localStorage.removeItem("clipboard");

    showClipboard();
}

function addSelectionToClipboard(action) {
    const selected = getSelectedItems().map(item => ({
        name: item.name,
        path: childPath(item.name),
        type: item.type,
        extension: item.extension,
        action: action
    }));

    // Adding an item again replaces its earlier entry, so it can switch between copy and cut
    const paths = new Set(selected.map(item => item.path));
    const clipboard = getClipboard().filter(item => !paths.has(item.path)).concat(selected);
    clipboard.sort((a, b) => a.name.localeCompare(b.name));
    saveClipboard(clipboard);

    // The items are on the clipboard now, so the checkboxes are free for the next action
    clearSelection();
}

function showClipboard() {
    const clipboard = getClipboard();

    const listItems = clipboard.map((item, index) => {
        const folder = item.path.slice(0, item.path.lastIndexOf('/')) || '/';
        return `
            <li class="clipboard-item">
                <span class="clipboard-icon" aria-hidden="true">${getFileTypeIcon(item.extension)}</span>
                <span class="clipboard-text">
                    <span class="clipboard-name">${htmlEncode(item.name)}</span>
                    <span class="clipboard-source">${htmlEncode(folder)}</span>
                </span>
                <span class="clipboard-action">${item.action === 'CUT' ? 'Cut' : 'Copy'}</span>
                <button type="button" class="icon-button" data-index="${index}" aria-label="Remove ${htmlEncode(item.name)} from the clipboard" title="Remove">&times;</button>
            </li>
        `;
    });

    $('#clipboard-list').html(listItems.join(''));
    $('#clipboard-count').text(clipboard.length);
    $('#clipboard').prop('hidden', clipboard.length === 0);
    updateToolbar();
}

function removeClipboardItem(index) {
    const clipboard = getClipboard();
    clipboard.splice(index, 1);
    saveClipboard(clipboard);
}

// Search

// Searches the folder specified in the URL. The results replace the listing until the search ends.
function runSearch(term) {
    term = term.trim();
    if (term.length === 0) {
        if (searchActive)
            clearSearch();
        return;
    }

    const folder = getPath();
    searchRequest?.abort();
    searchActive = true;
    hideBanner();
    updateToolbar();
    showSearchStatus(`Searching ${folder} for "${term}"…`);

    searchRequest = $.ajax({
        url: '/directory/search',
        type: 'GET',
        data: { path: folder, term: term },
        success: function (data) {
            showSearchResults(data, term, folder);
        },
        error: function (error, status) {
            if (status === 'abort')
                return; // replaced by a newer search, or the user left
            console.error("Error: ", error);
            showSearchStatus(`Couldn't search ${folder}.`);
            showBanner(`The search couldn't be completed. ${getErrorMessage(error)}`);
        }
    });
}

function showSearchResults(data, term, folder) {
    const rows = data.results.map(result => {
        const icon = result.isLink ? '🔗' : result.type === 0 ? '📁' : getFileTypeIcon(result.extension);
        const type = result.type === 0 ? 'folder' : 'file';
        const location = result.path.slice(0, result.path.lastIndexOf('/')) || '/';
        const localDate = (new Date(result.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
        return `
            <tr class="search-result">
                <td class="col-select"></td>
                <td class="col-icon">${icon}</td>
                <td class="col-name">
                    <a data-result-path="${htmlEncode(result.path)}" data-type="${result.isLink ? 'link' : type}">${htmlEncode(result.name)}</a>
                    <span class="result-location">in ${htmlEncode(location)}</span>
                </td>
                <td class="col-type">${htmlEncode(result.extension)}</td>
                <td class="col-modified">${localDate}</td>
                <td class="col-size">${htmlEncode(result.displaySize)}</td>
            </tr>
        `;
    });

    $('#table-data tbody').html(rows.join(''));
    updateSelection();

    const count = data.results.length;
    if (count === 0)
        showSearchStatus(`Nothing in ${folder} matches "${term}".`);
    else if (data.truncated)
        showSearchStatus(`Showing the first ${count} items in ${folder} that match "${term}". Search for something more specific to see the rest.`);
    else
        showSearchStatus(`${count} ${count === 1 ? 'item' : 'items'} in ${folder} ${count === 1 ? 'matches' : 'match'} "${term}".`);
}

function showSearchStatus(text) {
    $('#search-summary').text(text);
    $('#search-status').prop('hidden', false);
}

// Cancels a running search and hides its status. The caller shows the folder's listing.
function endSearch() {
    searchRequest?.abort();
    searchRequest = null;
    if (searchActive)
        $('#search-box').val('');
    searchActive = false;
    $('#search-status').prop('hidden', true);
}

// Goes back from search results to the folder's own listing
function clearSearch() {
    getDirectory();
}

// A folder opens itself. A file or link opens the folder it's in, with that item highlighted.
function openSearchResult(path, type) {
    const lastSlash = path.lastIndexOf('/');
    const folder = path.slice(0, lastSlash) || '/';
    if (type === 'folder')
        openFolder(path);
    else
        openFolder(folder, path.slice(lastSlash + 1));
}

function openFolder(path, highlightName = null) {
    hideBanner();
    pendingHighlight = highlightName === null ? null : { folder: path, name: highlightName };
    updateQueryParam('path', path, true);
    getDirectory();
}

// Highlights the search result that was opened, once its folder's listing is on screen
function highlightPendingResult() {
    const pending = pendingHighlight;
    pendingHighlight = null;
    if (pending === null || pending.folder !== shownPath)
        return;

    const row = $('#table-data input.row-check').filter((index, box) => box.dataset.name === pending.name).closest('tr');
    if (row.length === 0)
        return; // moved or deleted since the search

    const fileLink = row.find('a[data-type="file"]');
    if (fileLink.length > 0)
        setFileProperties(fileLink[0].dataset.path, fileLink[0]);
    else
        row.addClass('selected').siblings().removeClass('selected');
    row[0].scrollIntoView({ block: 'center' });
}

// Item actions

function pasteClipboard() {
    const clipboard = getClipboard();
    if (clipboard.length === 0)
        return;

    const copyItems = clipboard.filter(item => item.action === 'COPY').map(item => item.path);
    const cutItems = clipboard.filter(item => item.action === 'CUT').map(item => item.path);
    const overwrite = $('#overwrite').prop('checked');

    hideBanner();
    setActionRunning(true);
    $.ajax({
        url: '/directory/paste?' + $.param({ overwrite: overwrite }),
        type: 'POST',
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify({ path: shownPath, copy: copyItems, cut: cutItems }),
        success: function (result) {
            console.log("Success: ", result);
            // Drop the items that were pasted and keep the ones that failed, so they can be tried again
            const sentPaths = new Set(clipboard.map(item => item.path));
            const failedPaths = new Set(result.failed.map(failure => failure.path));
            saveClipboard(getClipboard().filter(item => !sentPaths.has(item.path) || failedPaths.has(item.path)));
            showActionResult(result, 'pasted');
            getDirectory();
        },
        error: function (error) {
            console.error("Error: ", error);
            showBanner(`The clipboard couldn't be pasted. ${getErrorMessage(error)}`);
        },
        complete: function () {
            setActionRunning(false);
        }
    });
}

function renameSelection() {
    const items = getSelectedItems();
    if (items.length === 0)
        return;

    // Several items share the new name, so the server numbers them
    const question = items.length === 1
        ? `Rename "${items[0].name}" to:`
        : `Rename ${items.length} items (numbered separately). New name:`;

    let newName = null;
    do {
        newName = prompt(question, items.length === 1 ? items[0].name : '');
        if (newName === null)
            return; // cancelled
        newName = newName.trim();
    } while (newName.length === 0);

    if (items.length === 1 && newName === items[0].name)
        return; // unchanged

    hideBanner();
    setActionRunning(true);
    $.ajax({
        url: '/directory/rename',
        type: 'POST',
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify({ path: shownPath, names: items.map(item => item.name), newName: newName }),
        success: function (result) {
            showActionResult(result, 'renamed');
            getDirectory();
        },
        error: function (error) {
            console.error("Error: ", error);
            showBanner(`${describeItems(items)} couldn't be renamed. ${getErrorMessage(error)}`);
        },
        complete: function () {
            setActionRunning(false);
        }
    });
}

function deleteSelection() {
    const items = getSelectedItems();
    if (items.length === 0)
        return;

    // Spell out what goes, since a folder takes everything in it with it
    const describe = item => item.isLink ? `${item.name} (link only)`
        : item.type === 'folder' ? `${item.name} (incl. all contents)`
        : item.name;
    const listed = items.slice(0, 10).map(item => `• ${describe(item)}`);
    if (items.length > 10)
        listed.push(`• ...and ${items.length - 10} more`);

    const question = items.length === 1
        ? `Delete ${describe(items[0])}?\n\nThis can't be undone.`
        : `Delete these ${items.length} items?\n\n${listed.join('\n')}\n\nThis can't be undone.`;
    if (!confirm(question))
        return;

    hideBanner();
    setActionRunning(true);
    $.ajax({
        url: '/directory/delete',
        type: 'POST',
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify({ path: shownPath, names: items.map(item => item.name) }),
        success: function (result) {
            showActionResult(result, 'deleted');
            getDirectory();
        },
        error: function (error) {
            console.error("Error: ", error);
            showBanner(`${describeItems(items)} couldn't be deleted. ${getErrorMessage(error)}`);
        },
        complete: function () {
            setActionRunning(false);
        }
    });
}

function downloadSelection() {
    const items = getSelectedItems();
    if (items.length === 0)
        return;

    if (items.some(item => item.isLink)) {
        showBanner("Links can't be downloaded. Uncheck them and try again.");
        return;
    }

    // The names travel in the address, and the server refuses addresses over about 8,000 characters
    const url = '/directory/download?' + $.param({ path: shownPath, names: items.map(item => item.name) }, true);
    if (url.length > maxDownloadUrlLength) {
        showBanner(`${items.length} items are too many to download at once. Select fewer, or download the folder they're in.`);
        return;
    }

    const link = document.createElement('a');
    link.href = url;
    link.download = ''; // keep the name the server gives it
    document.body.appendChild(link);
    link.click();
    link.remove();
}

// Kestrel refuses a request line over 8 KB, and the request line is the address plus a few words
const maxDownloadUrlLength = 8000;

// Match FileActionService.MaxUploadBytes and MaxUploadFiles, blocks upload over limit before it's sent
const maxUploadBytes = 100 * 1024 * 1024;
const maxUploadFiles = 1000;

function uploadFiles(files) {
    if (files.length === 0 || shownPath === null)
        return;

    if (files.length > maxUploadFiles) {
        showBanner(`That's ${files.length} files. Uploads are limited to ${maxUploadFiles} files at a time.`);
        return;
    }

    const totalBytes = files.reduce((total, file) => total + file.size, 0);
    if (totalBytes > maxUploadBytes) {
        showBanner(`Those files add up to ${Math.ceil(totalBytes / 1024 / 1024)} MB. Uploads are limited to ${maxUploadBytes / 1024 / 1024} MB at a time.`);
        return;
    }

    // Confirm before overwrite
    const listed = new Set($('#table-data input.row-check').map((index, box) => box.dataset.name.toLowerCase()).get());
    const conflicts = files.filter(file => listed.has(file.name.toLowerCase()));
    if (conflicts.length > 0) {
        const names = conflicts.slice(0, 10).map(file => `• ${file.name}`);
        if (conflicts.length > 10)
            names.push(`• ...and ${conflicts.length - 10} more`);
        const question = conflicts.length === 1 ? 'Replace this item?' : `Replace these ${conflicts.length} items?`;
        if (!confirm(`${question}\n\n${names.join('\n')}`))
            return;
    }

    const form = new FormData();
    files.forEach(file => form.append('files', file));

    hideBanner();
    setActionRunning(true);
    $.ajax({
        url: '/directory/upload?' + $.param({ path: shownPath, overwrite: conflicts.length > 0 }),
        type: 'POST',
        data: form,
        processData: false, // send the FormData as is
        contentType: false, // let the browser set multipart/form-data with its boundary
        dataType: 'json',
        success: function (result) {
            showActionResult(result, 'uploaded');
            getDirectory();
        },
        error: function (error) {
            console.error("Error: ", error);
            showBanner(`${files.length === 1 ? `"${files[0].name}"` : `${files.length} files`} couldn't be uploaded. ${getErrorMessage(error)}`);
        },
        complete: function () {
            setActionRunning(false);
        }
    });
}