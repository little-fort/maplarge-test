$(document).ready(function () {
    const path = getPath();
    if (!path || path.length === 0)
        updateQueryParam('path', '/', false);

    normalizePath();
    getDirectory();
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
            showContents(data.contents);
            setProperties(data.directory);
        },
        error: function (error) {
            console.error("Error: ", error);
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

function showContents(dir) {
    if (!dir || dir.length === 0)
        return "No directory contents returned."

    let tableRows = '';
    const path = getPath();

    if (path !== '/') {
        tableRows += `
            <tr>
                <td>📁</td>
                <td><a onclick="navigate('up');">.. (parent directory)</a></td>
                <td></td>
                <td></td>
                <td></td>
            </tr>
        `;
    }

    $.each(dir, function (index, object) {
        const objectType = object.type === 0 ? '📁' : '';
        const navLink = object.type === 0 ?
            "<a onclick=\"navigate('down', '" + object.name + "');\">" + object.name + "</a>" :
            "<a onclick=\"setFileProperties('" + object.fullPath + "');\">" + object.name + "</a>";
        const localDate = (new Date(object.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
        tableRows += `
            <tr>
                <td>${objectType}</td>
                <td>${navLink}</td>
                <td>${object.extension}</td>
                <td>${localDate}</td>
                <td>${object.displaySize}</td>
            </tr>
        `;
    })

    $('#table-data').html(tableRows);
}

function setFileProperties(fullPath) {
    const data = localStorage.getItem(getPath());
    if (data) {
        const directory = JSON.parse(data);
        const file = directory.contents.find(object => object.fullPath === fullPath);
        setProperties(file);
    }
    else
        console.error("Object cache deleted or expired. Reload page to refresh file details.");
}

function setProperties(item) {
    let icon = "";
    switch (item.extension) {
        case "EXE":
            icon = "💽";
            break;
        case "MP4":
        case "MOV":
        case "MPEG":
        case "AVI":
        case "MKV":
        case "WEBM":
            icon = "🎬";
            break;
        case "MP3":
        case "WAV":
        case "AAC":
        case "OGG":
            icon = "🔉";
            break;
        case "HTML":
            icon = "🌐";
        case "":
            icon = "📁";
            break;
        default:
            icon = "📄";
    }
    $("#properties-icon").html(icon);
    $("#properties-name").html(item.name);

    const localDate = (new Date(item.lastWriteTimeUtc)).toLocaleString('en-US', { timeZoneName: 'short' });
    const itemType = item.type === 0 ? "Folder" : item.extension;
    let tableRows = `
        <tr>
            <td>Last Modified</td>
            <td>${localDate}</td>
        </tr>
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
        `;
    }

    $('#properties-data').html(tableRows);
}