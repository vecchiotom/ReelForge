// njs script for cookie-based JWT auth translation

function getCookie(r, name) {
    var cookies = r.headersIn['Cookie'];
    if (!cookies) return '';
    var parts = cookies.split(';');
    for (var i = 0; i < parts.length; i++) {
        var pair = parts[i].trim().split('=');
        if (pair[0] === name) {
            return pair.slice(1).join('=');
        }
    }
    return '';
}

// Called via js_set to provide the Authorization header value
function getAuthHeader(r) {
    var token = getCookie(r, 'reelforge_token');
    if (token) {
        return 'Bearer ' + token;
    }
    return '';
}

// Handle logout: clear cookies and return 200
function handleLogout(r) {
    r.headersOut['Set-Cookie'] = [
        'reelforge_token=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0',
        'reelforge_user=; Path=/; SameSite=Lax; Max-Age=0'
    ];
    r.headersOut['Content-Type'] = 'application/json';
    r.return(200, '{"ok":true}');
}

export default { getAuthHeader, handleLogout };
