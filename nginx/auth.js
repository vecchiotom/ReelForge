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

// Intercept login response: extract accessToken, set cookies
function handleLoginResponse(r, data, flags) {
    // Accumulate chunks
    if (!r._responseBody) {
        r._responseBody = '';
    }
    r._responseBody += data;

    if (flags.last) {
        var body = r._responseBody;

        try {
            var json = JSON.parse(body);
            if (json.accessToken) {
                var userInfo = JSON.stringify({
                    email: json.email || '',
                    isAdmin: json.isAdmin || false,
                    mustChangePassword: json.mustChangePassword || false
                });

                r.headersOut['Set-Cookie'] = [
                    'reelforge_token=' + json.accessToken + '; Path=/; HttpOnly; SameSite=Lax; Max-Age=86400',
                    'reelforge_user=' + encodeURIComponent(userInfo) + '; Path=/; SameSite=Lax; Max-Age=86400'
                ];
            }
        } catch (e) {
            // Not JSON or parse error — pass through unchanged
        }

        r.sendBuffer(body, flags);
    }
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

export default { getAuthHeader, handleLoginResponse, handleLogout };
