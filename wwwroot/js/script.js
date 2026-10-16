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
            showContents(data.directory);
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
                <td><a onclick="navigate('up');">..</a></td>
                <td></td>
                <td></td>
                <td></td>
            </tr>
        `;
    }

    $.each(dir, function (index, object) {
        const objectType = object.type === 0 ? '📁' : '';
        const navLink = object.type === 0 ? "<a onclick=\"navigate('down', '" + object.name + "');\">" + object.name + "</a>" : object.name;
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