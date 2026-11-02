import urllib.request
import urllib.error

def replay_check(base_url: str) -> bool:
    """Returns True if the auth bypass via X-Impersonate-User header still works."""
    url = f"{base_url.rstrip('/')}/admin"
    req = urllib.request.Request(url, headers={"X-Impersonate-User": "1"})
    try:
        with urllib.request.urlopen(req, timeout=5) as res:
            return res.status == 200
    except urllib.error.HTTPError as e:
        return e.code == 200
    except Exception:
        return True

def variant_check(base_url: str) -> bool:
    """Returns True if CORS wildcard and header impersonation variations are blocked."""
    url = f"{base_url.rstrip('/')}/admin"
    for header in ["x-impersonate-user", "X-IMPERSONATE-USER"]:
        req = urllib.request.Request(url, headers={header: "1"})
        try:
            with urllib.request.urlopen(req, timeout=5) as res:
                if res.status == 200:
                    return False
        except urllib.error.HTTPError as e:
            if e.code == 200:
                return False
        except Exception:
            pass
    return True

def regression_check(base_url: str) -> bool:
    """Returns True if legitimate admin login still works with password."""
    login_url = f"{base_url.rstrip('/')}/login"
    data = b"username=admin&password=change-me-locally"
    req = urllib.request.Request(login_url, data=data, method="POST",
                                 headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor())
        with opener.open(req, timeout=5) as res:
            pass
        with opener.open(f"{base_url.rstrip('/')}/admin", timeout=5) as res:
            return res.status == 200
    except Exception:
        return False

