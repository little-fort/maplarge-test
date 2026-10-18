$(document).ready(function () {
    const path = getPath();
    if (!path || path.length === 0 || path.includes(':') || path.includes(".."))
        updateQueryParam('path', '/', false);

    normalizePath();
    getDirectory();

    window.addEventListener('popstate', (event) => {
        getDirectory();
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
    $.ajax({
        url: '/directory',
        type: 'GET',
        data: "path=" + path,
        success: function (data) {
            console.log("Success: ", data);
            localStorage.setItem(getPath(), JSON.stringify(data));
            showLocation(path);
            showContents(data.contents);
            setProperties(data.directory);
        },
        error: function (error) {
            console.error("Error: ", error);
            alert("The specified path was not found on the server.");
            updateQueryParam('path', '/', false);
            getDirectory();
        }
    });
}

function navigate(direction, name) {
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
        const objectType = object.type === 0 ? '📁' : '';
        const navLink = object.type === 0 ?
            "<a onclick=\"navigate('down', '" + encodeURIComponent(object.name) + "');\">" + object.name + "</a>" :
            "<a onclick=\"setFileProperties('" + htmlEncode(object.fullPath) + "', this);\">" + object.name + "</a>";
        const localDate = (new Date(object.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
        tableRows += `
            <tr>
                <td class="col-icon">${objectType}</td>
                <td class="col-name">${navLink}</td>
                <td class="col-type">${object.extension}</td>
                <td class="col-modified">${localDate}</td>
                <td class="col-size">${object.displaySize}</td>
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
    $("#properties-name").html(item.name);

    const localDate = (new Date(item.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
    const itemType = item.type === 0 ? "Folder" : item.extension;
    let tableRows = `
        <tr>
            <td>Type</td>
            <td>${itemType}</td>
        </tr>
    `;

    if (item.type === 0) {
        tableRows += `
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
    if (extension === "FILE") return "📄";

    const exe = ["EXE"]
    const cab = ["CAB", "MSI", "MSIX", "ZIP", "7Z"];
    const video = ["MP4", "MOV", "MPEG", "AVI", "MKV", "WEBM"];
    const audio = ["MP3", "WAV", "AAC", "OGG"];
    const web = ["HTM", "HTML"];
    const script = ["JS", "PS1", "PY", "SH", "BAT"];

    if (exe.includes(extension)) return "💽";
    if (cab.includes(extension)) return "🗄️";
    if (video.includes(extension)) return "🎬";
    if (audio.includes(extension)) return "🔉";
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