// Folder sizes load separately from the listing (see getFolderSizes). Keeping the request
// lets it be cancelled when the user leaves the folder, which also stops the server's walk.
let folderSizesRequest = null;
const sizePlaceholder = '<span class="size-pending" title="Calculating size…">…</span>';

$(document).ready(function () {
    const path = getPath();
    if (!path || path.length === 0 || path.includes(':') || path.includes(".."))
        updateQueryParam('path', '/', false);

    normalizePath();
    getDirectory();
    updateClipboardDisplay();

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
    folderSizesRequest?.abort(); // the sizes are for the folder being left
    $.ajax({
        url: '/directory',
        type: 'GET',
        data: { path: path },
        success: function (data) {
            console.log("Success: ", data);
            localStorage.setItem(getPath(), JSON.stringify(data));
            showLocation(path);
            showContents(data.contents);
            setProperties(data.directory);
            getFolderSizes(path);
        },
        error: function (error) {
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

    $('#table-data a[data-type="folder"]').each(function () {
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

function showBanner(message) {
    $('#banner').prop('hidden', false);
    $('#banner-message').text(message);
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
                <td class="col-icon">📁</td>
                <td class="col-name"><a onclick="navigate('up');">.. (parent directory)</a></td>
                <td class="col-type"></td>
                <td class="col-modified"></td>
                <td class="col-size"></td>
            </tr>
        `;
    }

    $.each(dir, function (index, object) {
        const objectIcon = object.type === 0 ? '📁' : '';
        const objectType = object.type === 0 ? 'folder' : 'file';
        const navLink = `<a data-type="${objectType}" data-name="${htmlEncode(object.name)}" data-path="${htmlEncode(object.fullPath)}">${htmlEncode(object.name)}</a>`;
        const localDate = (new Date(object.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
        const objectSize = object.type === 0 ? sizePlaceholder : object.displaySize;
        tableRows += `
            <tr>
                <td class="col-icon">${objectIcon}</td>
                <td class="col-name">${navLink}</td>
                <td class="col-type">${htmlEncode(object.extension)}</td>
                <td class="col-modified">${localDate}</td>
                <td class="col-size">${objectSize}</td>
            </tr>
        `;
    })

    $('#table-data tbody').html(tableRows);
}

function setFileProperties(fullPath, link) {
    const data = localStorage.getItem(getPath());
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
    localStorage.setItem("selectedItem", JSON.stringify(item));
}

function getFileTypeIcon(extension) {
    if (extension === "") return "📁";
    if (extension === "FILE") return "📄";

    const exe = ["EXE"]
    const cab = ["CAB", "MSI", "MSIX", "ZIP", "7Z"];
    const video = ["MP4", "MOV", "MPEG", "AVI", "MKV", "WEBM"];
    const audio = ["MP3", "WAV", "AAC", "OGG"];
    const image = ["JPEG", "JPG", "PNG", "TIFF", "BMP", "GIF"];
    const web = ["HTM", "HTML"];
    const script = ["JS", "PS1", "PY", "SH", "BAT"];

    if (exe.includes(extension)) return "💽";
    if (cab.includes(extension)) return "🗄️";
    if (video.includes(extension)) return "🎬";
    if (audio.includes(extension)) return "🔉";
    if (image.includes(extension)) return "🖼️";
    if (web.includes(extension)) return "🌐";
    if (script.includes(extension)) return "📜";

    return "📃";
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

// Clears the results of the previous action so they aren't mistaken for this one's
function clearActionResults() {
    hideBanner();
    $('#action-success-result').text('');
    $('#action-failed-message').text('');
    $('#action-failed-list ul').empty();
}

// Lists the clipboard items the server couldn't paste
function showFailedItems(names) {
    if (names.length === 1) {
        $('#action-failed-message').text(`${names[0]} could not be pasted into the specified directory.`);
        return;
    }

    $('#action-failed-message').text('The following items could not be pasted into the specified directory:');
    $('#action-failed-list ul').html(names.map(name => `<li>${htmlEncode(name)}</li>`).join(''));
}

// Item actions
function getClipboard() {
    const clipboardString = localStorage.getItem("clipboard");
    const clipboard = clipboardString ? JSON.parse(clipboardString) : [];
    return clipboard;
}
function addItemToClipboard(action) {
    const clipboard = getClipboard();
    const itemString = localStorage.getItem("selectedItem");

    if (!itemString) {
        showBanner("There was an error updating the clipboard. Reload the page and try again.");
        return;
    }

    const item = JSON.parse(itemString);
    item.action = action;

    if (!clipboard.some(clip => clip.fullPath == item.fullPath)) {
        clipboard.push(item);
        clipboard.sort((a, b) => a.name.localeCompare(b.name));
        localStorage.setItem("clipboard", JSON.stringify(clipboard));

        updateClipboardDisplay();
    }
}

function pasteClipboard() {
    const clipboard = getClipboard();
    const copyItems = clipboard.filter(object => object.action === 'COPY');
    const cutItems = clipboard.filter(object => object.action === 'CUT');
    const overwrite = $('#overwrite').prop('checked') ? 'true' : 'false'

    clearActionResults();
    $.ajax({
        url: '/directory/paste?' + $.param({ overwrite: overwrite }),
        type: 'POST',
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify({ path: getPath(), copy: copyItems, cut: cutItems }),
        success: function (data) {
            console.log("Success: ", data);
            getDirectory();
            localStorage.removeItem('clipboard');
            updateClipboardDisplay();
            $('#action-success-result').text(data.message);
        },
        error: function (error) {
            console.error("Error: ", error);
            const result = getErrorBody(error);

            if (result && Array.isArray(result.failed) && result.failed.length > 0) {
                // Some items failed. Any that succeeded are already in place, so show
                // their count and refresh the listing.
                $('#action-success-result').text(result.message);
                showFailedItems(result.failed);
                getDirectory();
            }
            else
                showBanner(`The clipboard couldn't be pasted. ${getErrorMessage(error)}`);
        }
    });
}

function updateClipboardDisplay() {
    let tableRows = '';
    const clipboard = getClipboard();

    if (clipboard && clipboard.length > 0) {
        $.each(clipboard, function (index, object) {
            const objectIcon = getFileTypeIcon(object.extension);
            tableRows += `
            <tr>
                <td class="col-icon">${objectIcon}</td>
                <td class="col-name">${object.name}</td>
                <td>${object.action}</td>
                <td onclick="removeClipboardItem('${index}')">x</td>
            </tr>
        `;
        })

        $('#clipboard-display tbody').html(tableRows);
        $('#clipboard-overwrite').show();
    }
    else {
        $('#clipboard-display tbody').html('');
        $('#clipboard-overwrite').hide();
    }
}

function removeClipboardItem(index) {
    const clipboard = getClipboard();

    if (clipboard.length === 0 && index === 0)
        localStorage.removeItem('clipboard');
    else {
        clipboard.splice(index, 1);
        localStorage.setItem("clipboard", JSON.stringify(clipboard));
    }

    updateClipboardDisplay();
}

function renameItem() {
    const minLength = 1;
    let input = null;

    do {
        input = prompt(`Enter desired file name:`);

        if (input === null) {
            break;
        }
    } while (input.trim().length < minLength);

    if (input !== null) {
        const itemString = localStorage.getItem("selectedItem");
        const item = JSON.parse(itemString);

        clearActionResults();
        $.ajax({
            url: '/directory/rename',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ path: item.fullPath, name: input.trim() }),
            success: function () {
                getDirectory();
                $('#action-success-result').text('Item renamed successfully.');
            },
            error: function (error) {
                console.error("Error: ", error);
                showBanner(`"${item.name}" couldn't be renamed. ${getErrorMessage(error)}`);
            }
        });
    } else {
        console.log("User cancelled the prompt.");
    }
}

function deleteItem() {
    const confirmed = confirm("Are you sure you want to delete this item?");

    if (confirmed) {
        const itemString = localStorage.getItem("selectedItem");
        const item = JSON.parse(itemString);

        clearActionResults();
        $.ajax({
            url: '/directory?' + $.param({ path: item.fullPath }),
            type: 'DELETE',
            success: function () {
                $('#action-success-result').text('Item deleted successfully.');

                if (item.fullPath.endsWith(getPath()))
                    navigate('up');
                else
                    getDirectory();
            },
            error: function (error) {
                console.error("Error: ", error);
                showBanner(`"${item.name}" couldn't be deleted. ${getErrorMessage(error)}`);
            }
        });
    } else {
        console.log("Action cancelled by the user.");
    }
}