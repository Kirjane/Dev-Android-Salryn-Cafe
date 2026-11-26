"""Real ADB/UIAutomator driver, restricted to the named disposable QA emulator."""
import argparse
import json
import re
import ssl
import subprocess
import time
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("marker")
parser.add_argument("--adb", default="C:/Users/arane/AppData/Local/Android/Sdk/platform-tools/adb.exe")
parser.add_argument("--serial", default="emulator-5554")
parser.add_argument(
    "--stage",
    choices=[
        "inspect",
        "setup",
        "login",
        "open-shift",
        "park-resume",
        "ready-order",
        "resume-ready",
        "post-cash",
        "post-credit",
        "cash",
        "credit",
        "partial-credit",
        "refund",
        "start-another",
        "reconnect",
        "close-shift",
        "revoke-check",
    ],
    default="inspect",
)
parser.add_argument("--counted-cash", default="5.00")
args = parser.parse_args()
marker = json.loads(Path(args.marker).read_text())
assert marker["isDisposableTest"] is True
root = Path(marker["root"]).resolve()
assert Path(marker["databasePath"]).resolve().is_relative_to(root)
evidence = root / "ui"
evidence.mkdir(exist_ok=True)


def adb(*parts, binary=False):
    result = subprocess.run([args.adb, "-s", args.serial, *parts], capture_output=True, timeout=45, check=True)
    return result.stdout if binary else result.stdout.decode(errors="replace")


assert adb("emu", "avd", "name").splitlines()[0].strip() == "salryn_cafe_qa_api35", "Refusing non-QA device"
width, height = map(int, re.findall(r"(\d+)x(\d+)", adb("shell", "wm", "size"))[-1])


def dump():
    for _ in range(5):
        try:
            adb("shell", "uiautomator", "dump", "--compressed", "/data/local/tmp/salryn-cafe-ui.xml")
            content = adb("shell", "cat", "/data/local/tmp/salryn-cafe-ui.xml")
            return ET.fromstring(content)
        except (subprocess.CalledProcessError, ET.ParseError):
            time.sleep(1)
    raise RuntimeError("Native UI dump did not become available")


def screenshot(label):
    (evidence / f"{label}.png").write_bytes(adb("exec-out", "screencap", "-p", binary=True))
    ET.ElementTree(dump()).write(evidence / f"{label}.xml", encoding="utf-8")


def swipe(up=True):
    x, top, bottom = str(width // 2), str(int(height * 0.25)), str(int(height * 0.78))
    adb("shell", "input", "swipe", x, bottom if up else top, x, top if up else bottom, "350")
    time.sleep(0.35)


def scroll_to_top():
    for _ in range(6):
        swipe(False)


def find(label, kind=None, prefix=False):
    def match():
        for node in dump().iter("node"):
            if any(
                ((v or "").casefold().startswith(label.casefold()) if prefix else label.casefold() == (v or "").casefold())
                for v in (node.get("content-desc"), node.get("text"))
            ) and (kind is None or kind in node.get("class", "")):
                bounds = list(map(int, re.findall(r"\d+", node.get("bounds", ""))))
                if len(bounds) == 4 and bounds[2] > bounds[0] and bounds[3] > bounds[1]:
                    return bounds
        return None

    found = match()
    if found:
        return found
    for direction, count in [(True, 8), (False, 16), (True, 8)]:
        for _ in range(count):
            swipe(direction)
            found = match()
            if found:
                return found
    raise AssertionError(f"Native control not found: {label}")


def click(label, kind=None, prefix=False):
    print("Native action: " + label, flush=True)
    x1, y1, x2, y2 = find(label, kind, prefix)
    adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
    time.sleep(0.6)


def fill(label, value):
    click(label, "EditText")
    adb("shell", "input", "keycombination", "113", "29")
    adb("shell", "input", "keyevent", "KEYCODE_DEL")
    # Reject shell metacharacters in synthetic fixtures.
    assert not any(x in value for x in "&;|`$\n\r<>")
    adb("shell", "input", "text", value.replace(" ", "%s"))
    adb("shell", "input", "keyevent", "4")
    time.sleep(0.3)


ca = Path(marker["caPath"]).read_text()
context = ssl.create_default_context(cadata=ca)


def http(path, payload=None, token=None):
    request = urllib.request.Request(
        marker["loopbackUrl"] + path,
        data=None if payload is None else json.dumps(payload).encode(),
        headers={
            "Content-Type": "application/json",
            "X-Salryn-Product-Id": "Salryn.Cafe",
            **({"Authorization": "Bearer " + token} if token else {}),
        },
    )
    with urllib.request.urlopen(request, context=context, timeout=15) as response:
        raw = response.read()
        return json.loads(raw) if raw else {}


if args.stage == "setup":
    screenshot("01-login-initial")
    click("Device setup and synchronization", "Button")
    admin_token = http(
        "/api/sync/v1/admin/auth/login",
        {"username": marker["adminUsername"], "password": marker["adminPassword"]},
    )["accessToken"]
    invite = http("/api/sync/v1/admin/devices/invites", {"deviceRole": "OfflineAndroidCashier"}, admin_token)
    for label, value in [
        ("Approved Central HTTPS origin", marker["androidUrl"]),
        ("Tenant ID", marker["tenantId"]),
        ("Store ID", marker["storeId"]),
        ("Independently approved server public key (SPKI base64)", marker["pinnedServerKey"]),
        ("One-time join code", invite["joinCode"]),
        ("Device name", "Disposable Android Cashier"),
    ]:
        fill(label, value)
    click("Request device enrollment", "Button")
    time.sleep(2)
    devices = http("/api/sync/v1/admin/devices", token=admin_token)
    if isinstance(devices, dict):
        devices = devices.get("devices", devices.get("items", []))
    matching = [d for d in devices if d.get("deviceName") == "Disposable Android Cashier"]
    assert len(matching) == 1, "Expected one actual native device enrollment"
    http("/api/sync/v1/admin/devices/" + matching[0]["deviceId"] + "/approve", {}, admin_token)
    click("Connect and bootstrap approved device", "Button")
    time.sleep(2)
    scroll_to_top()
    screenshot("02-bootstrap")
    fill("Central operator username", marker["operatorUsername"])
    fill("Central operator password", marker["operatorPassword"])
    click("Enroll operator for offline sign in", "Button")
    time.sleep(2)
    scroll_to_top()
    screenshot("03-operator-enrollment")
    click("Back to operator sign in", "Button")
    print("Native setup actions executed; screenshots and subsequent login must confirm outcomes.")
elif args.stage == "login":
    fill("Username", marker["operatorUsername"])
    fill("Password", marker["operatorPassword"])
    click("Sign in", "Button")
    time.sleep(1.5)
    scroll_to_top()
    screenshot("04-local-login")
    find("Refresh menu and orders", "Button")
    print("Native local sign-in reached cashier; menu/financial lifecycle still require proof.")
elif args.stage == "open-shift":
    fill("Opening cash (PHP)", "100.00")
    click("Open / recover my shift", "Button")
    time.sleep(1)
    scroll_to_top()
    screenshot("04-shift-open")
    print("Native shift open/recover executed; verify whether a genuinely new session was created.")
elif args.stage == "start-another":
    click("Start another order", "Button")
elif args.stage == "park-resume":
    click("Service mode: Takeout", "Button", prefix=True)
    fill("Table / Buzzer / Order Ref", "Table 5")
    click("C4 Coffee · PHP 5.00", "Button")
    click("Add to order", "Button")
    click("Save order", "Button")
    time.sleep(0.8)
    click("Park order", "Button")
    time.sleep(0.8)
    scroll_to_top()
    screenshot("05a-parked-order")
    click("Resume order", "Button")
    time.sleep(0.8)
    find("Send to preparation", "Button")
    click("Start another order", "Button")
    print("Native Dine-In Park/Resume lifecycle executed.")
elif args.stage == "resume-ready":
    click("Takeout · Ready", "Button", prefix=True)
    find("Checkout · Cash / Accounts Receivable", "Button")
    screenshot("05-restored-ready-order")
elif args.stage in ("ready-order", "cash", "credit", "partial-credit", "post-cash", "post-credit"):
    if args.stage in ("ready-order", "cash", "credit", "partial-credit"):
        click("C4 Coffee · PHP 5.00", "Button")
        click("Add to order", "Button")
        click("Save order", "Button")
        click("Send to preparation", "Button")
        click("Mark preparing", "Button")
        click("Mark ready", "Button")
    if args.stage == "ready-order":
        screenshot("05-offline-ready-order")
        print("Native ready order saved; process restart/recovery still requires proof.")
    else:
        is_credit = args.stage in ("credit", "post-credit")
        is_partial = args.stage == "partial-credit"
        click("Checkout · Cash / Accounts Receivable", "Button")
        if is_partial:
            fill("Cash received", "2.00")
            fill("Accounts Receivable amount", "3.00")
        else:
            fill("Cash received", "0" if is_credit else "5.00")
            fill("Accounts Receivable amount", "5.00" if is_credit else "0")
        if is_credit or is_partial:
            click("Credit customer", "Spinner")
            click("Synthetic Android Customer")
        click("Post sale", "Button")
        time.sleep(1.2)
        find("Print customer receipt", "Button")
        screenshot("05-" + args.stage + "-sale")
        print("Native order lifecycle and tender posting reached receipt action; verify persisted ledgers separately.")
elif args.stage == "refund":
    click("Audited Refunds (C5)", "Button")
    time.sleep(1)
    screenshot("06a-refunds-page")
    click("INV-", "Button", prefix=True)
    time.sleep(0.6)
    fill("Refund reason", "QA emulator refund proof")
    click("Request Refund", "Button")
    time.sleep(1.2)
    click("Approve refund ", "Button", prefix=True)
    time.sleep(0.6)
    click("Confirm Approval", "Button")
    time.sleep(1.2)
    click("Commit refund ", "Button", prefix=True)
    time.sleep(0.6)
    click("Commit Cash / AR", "Button")
    time.sleep(1.2)
    scroll_to_top()
    screenshot("06b-refund-committed")
    click("Back to Cashier", "Button")
    print("Native C5 refund Request -> Approve -> Cash Commit executed.")
elif args.stage == "reconnect":
    click("Device setup and synchronization", "Button")
    fill("Central operator username", marker["operatorUsername"])
    fill("Central operator password", marker["operatorPassword"])
    click("Reconnect and synchronize pending operations", "Button")
    time.sleep(2.5)
    scroll_to_top()
    screenshot("07-reconnect")
    click("Back to Cashier", "Button")
    print("Native reconnect action executed; verify durable Central and local receipts separately.")
elif args.stage == "close-shift":
    fill("Physical counted cash (PHP)", args.counted_cash)
    click("Close my shift with physical count", "Button")
    time.sleep(1.2)
    scroll_to_top()
    screenshot("08-shift-close")
    print("Native blind shift-close action executed; verify persisted count separately.")
elif args.stage == "revoke-check":
    admin_token = http(
        "/api/sync/v1/admin/auth/login",
        {"username": marker["adminUsername"], "password": marker["adminPassword"]},
    )["accessToken"]
    devices = http("/api/sync/v1/admin/devices", token=admin_token)
    if isinstance(devices, dict):
        devices = devices.get("devices", devices.get("items", []))
    matching = [d for d in devices if d.get("deviceName") == "Disposable Android Cashier"]
    assert len(matching) == 1, "Expected one approved device to revoke"
    http("/api/sync/v1/admin/devices/" + matching[0]["deviceId"] + "/revoke", {"reason": "QA revocation proof"}, admin_token)
    click("Device setup and synchronization", "Button")
    fill("Central operator username", marker["operatorUsername"])
    fill("Central operator password", marker["operatorPassword"])
    click("Reconnect and synchronize pending operations", "Button")
    time.sleep(2)
    scroll_to_top()
    screenshot("09-revoked-fenced")
    print("Native revocation fencing executed; verify REVOKED FENCED state in screenshot and local DB.")
else:
    screenshot("01-inspection")
    print("Native inspection captured.")
