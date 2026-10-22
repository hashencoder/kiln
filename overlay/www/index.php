<?php
/**
 * WebHut dashboard - shown at http://localhost
 * Lists your sites (folders in www\) and Node apps, and what's running.
 * webhut-dashboard: 2
 * WebHut updates this file when it is unchanged; your own edits are backed up first.
 */
declare(strict_types=1);

if (isset($_GET['phpinfo'])) {
    phpinfo();
    exit;
}

$root = $_SERVER['WEBHUT_ROOT'] ?? dirname(__DIR__);
$port = (int)($_SERVER['SERVER_PORT'] ?? 80);
$suffix = $port === 80 ? '' : ':' . $port;
$dbPort = (int)($_SERVER['WEBHUT_MYSQL_PORT'] ?? 3306);

function h(string $s): string
{
    return htmlspecialchars($s, ENT_QUOTES, 'UTF-8');
}

/** What a folder looks like it contains, from a few tell-tale files. */
function detect(string $dir): array
{
    $has = fn(string $p) => file_exists($dir . '/' . $p);
    if ($has('artisan') && $has('bootstrap/app.php')) return ['Laravel', 'laravel'];
    if ($has('wp-config.php') || $has('wp-load.php')) return ['WordPress', 'wordpress'];
    if ($has('bin/console') && ($has('symfony.lock') || $has('config/bundles.php'))) return ['Symfony', 'symfony'];
    if ($has('spark') && $has('app/Config/App.php')) return ['CodeIgniter', 'codeigniter'];
    if ($has('core/lib/Drupal.php') || $has('web/core/lib/Drupal.php')) return ['Drupal', 'drupal'];
    if ($has('administrator') && $has('libraries/src')) return ['Joomla', 'joomla'];
    if ($has('craft')) return ['Craft CMS', 'craft'];
    if ($has('package.json') && !$has('composer.json')) return ['Node / JS', 'node'];
    if ($has('composer.json')) return ['PHP (Composer)', 'php'];
    foreach (['index.php', 'public/index.php'] as $f) if ($has($f)) return ['PHP', 'php'];
    foreach (['index.html', 'public/index.html'] as $f) if ($has($f)) return ['Static HTML', 'html'];
    return ['Folder', 'folder'];
}

$sites = [];
foreach (scandir(__DIR__) ?: [] as $entry) {
    if ($entry[0] === '.' || !is_dir(__DIR__ . '/' . $entry)) continue;
    [$kind, $kindId] = detect(__DIR__ . '/' . $entry);
    $sites[] = [
        'name'   => $entry,
        'valid'  => (bool)preg_match('/^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/', $entry),
        'public' => is_dir(__DIR__ . '/' . $entry . '/public'),
        'kind'   => $kind,
        'kindId' => $kindId,
        'mtime'  => (int)@filemtime(__DIR__ . '/' . $entry),
    ];
}

// Network access (Settings -> "Allow access from other devices")
$lanOn = isset($_SERVER['WEBHUT_LAN_IPS']);
$lanIps = array_values(array_filter(explode(',', (string)($_SERVER['WEBHUT_LAN_IPS'] ?? '')),
    fn($ip) => (bool)filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)));

$nodeApps = [];
$appsFile = $root . '/node-apps.json';
if (is_file($appsFile)) {
    $data = json_decode((string)file_get_contents($appsFile), true);
    foreach ((is_array($data['apps'] ?? null) ? $data['apps'] : []) as $app) {
        $p = (int)($app['port'] ?? 0);
        $up = false;
        if ($p > 0) {
            $s = @fsockopen('127.0.0.1', $p, $errno, $errstr, 0.15);
            if ($s) { $up = true; fclose($s); }
        }
        $nodeApps[] = ['name' => (string)($app['name'] ?? ''), 'port' => $p, 'command' => (string)($app['command'] ?? ''), 'up' => $up];
    }
}

$dbState = 'down';
$dbText = 'Not running';
$dbVersion = '';
if (class_exists('mysqli')) {
    mysqli_report(MYSQLI_REPORT_OFF);
    $mysqli = @mysqli_init();
    if ($mysqli) {
        @$mysqli->options(MYSQLI_OPT_CONNECT_TIMEOUT, 2);
        if (@$mysqli->real_connect('127.0.0.1', 'root', '', '', $dbPort)) {
            $dbState = 'up';
            $dbVersion = preg_replace('/-.*$/', '', (string)$mysqli->server_info);
            $dbText = 'Running · root has no password';
            $mysqli->close();
        } elseif ((int)$mysqli->connect_errno === 1045) {
            $dbState = 'up';
            $dbText = 'Running · root password set';
        }
    }
} else {
    $dbState = 'warn';
    $dbText = 'mysqli extension not enabled';
}

$apache = explode(' ', (string)($_SERVER['SERVER_SOFTWARE'] ?? 'Apache'))[0];
$apacheVersion = str_contains($apache, '/') ? explode('/', $apache)[1] : '';
$exts = array_map('strtolower', get_loaded_extensions());
$keyExts = ['mysqli', 'pdo_mysql', 'mbstring', 'intl', 'curl', 'gd', 'zip', 'openssl', 'fileinfo', 'sodium', 'zend opcache', 'xdebug'];
$sapi = PHP_SAPI === 'apache2handler' ? 'mod_php' : (str_contains(PHP_SAPI, 'cgi') ? 'FastCGI' : PHP_SAPI);
$allUp = $dbState === 'up';
$validSites = count(array_filter($sites, fn($s) => $s['valid']));
usort($sites, fn($a, $b) => strcasecmp($a['name'], $b['name']));
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>WebHut · localhost</title>
<link rel="icon" href="data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><rect width='64' height='64' rx='16' fill='%23EA4B0C'/><path d='M33 12c2 9 13 14 13 27a14 14 0 0 1-28 0c0-7 4-11 7-14 0 5 2 8 5 9-2-8 1-15 3-22z' fill='white'/></svg>">
<style>
  :root {
    --bg:#F6F8FB; --card:#FFFFFF; --surface:#EEF2F6; --line:#E2E7ED; --ink:#17212F; --muted:#667487; --faint:#8793A3;
    --accent:#EA4B0C; --accent-ink:#C93F08; --accent-soft:#FFF0E8;
    --green:#16A36A; --green-soft:#E3F6EE; --amber:#D99516; --amber-soft:#FCF1DC; --red:#DC4545; --red-soft:#FCE7E7; --grey:#8A96A6; --grey-soft:#EEF1F4;
    --mono: "JetBrains Mono", "Cascadia Mono", Consolas, ui-monospace, monospace;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg:#10151D; --card:#1B2430; --surface:#242F3D; --line:#344152; --ink:#E9EEF4; --muted:#9AA7B8; --faint:#7D8A9B;
      --accent:#EA4B0C; --accent-ink:#FF762E; --accent-soft:#38251E;
      --green-soft:#16342A; --amber-soft:#3A2F18; --red-soft:#3D1F22; --grey-soft:#2A3442;
    }
  }
  * { box-sizing: border-box; }
  [hidden] { display: none !important; }
  html { -webkit-text-size-adjust: 100%; }
  body { margin: 0; background: var(--bg); color: var(--ink); font: 14px/1.5 "Inter", "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif; }
  a { color: var(--accent-ink); text-decoration: none; }
  a:hover { text-decoration: underline; }
  code, .mono { font-family: var(--mono); font-size: 12.5px; }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 32px 24px 48px; }

  header { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; margin-bottom: 28px; }
  .logo { width: 48px; height: 48px; flex: none; }
  .title { flex: 1; min-width: 220px; }
  .title h1 { margin: 0; font-size: 28px; line-height: 1.2; font-weight: 700; letter-spacing: -0.01em; }
  .title p { margin: 2px 0 0; color: var(--muted); }
  .chip { display: inline-flex; align-items: center; gap: 8px; padding: 6px 12px; border-radius: 999px; font-weight: 600; font-size: 13px; white-space: nowrap; }
  .chip::before { content: ""; width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
  .chip.up { color: var(--green); background: var(--green-soft); }
  .chip.warn { color: var(--amber); background: var(--amber-soft); }
  .chip.down { color: var(--grey); background: var(--grey-soft); }

  .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 14px; margin-bottom: 28px; }
  .card { background: var(--card); border: 1px solid var(--line); border-radius: 12px; }
  .stat { padding: 16px 18px; display: flex; flex-direction: column; gap: 6px; }
  .stat .k { display: flex; align-items: center; justify-content: space-between; color: var(--muted); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .05em; }
  .stat .v { font-size: 20px; font-weight: 600; }
  .stat .s { color: var(--muted); font-size: 13px; }
  .dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; }
  .dot.up { background: var(--green); } .dot.down { background: var(--grey); } .dot.warn { background: var(--amber); }

  .actions { display: flex; gap: 10px; flex-wrap: wrap; margin-bottom: 32px; }
  .btn { display: inline-flex; align-items: center; gap: 8px; padding: 9px 14px; border-radius: 8px; border: 1px solid var(--line); background: var(--card); color: var(--ink); font-weight: 600; font-size: 13px; }
  .btn:hover { text-decoration: none; background: var(--surface); }
  .btn.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
  .btn.primary:hover { background: #C93F08; }
  .btn svg { width: 16px; height: 16px; flex: none; }

  section { margin-bottom: 32px; }
  .sec-head { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
  h2 { margin: 0; font-size: 20px; font-weight: 600; }
  h2 small { color: var(--muted); font-weight: 400; font-size: 14px; margin-left: 6px; }
  .search { width: 260px; max-width: 100%; padding: 8px 12px; border-radius: 8px; border: 1px solid var(--line); background: var(--card); color: var(--ink); font: inherit; }
  .search:focus { outline: 2px solid var(--accent-soft); border-color: var(--accent); }

  .sites { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px; }
  .site { color: var(--ink); padding: 14px 16px; display: flex; flex-direction: column; gap: 8px; transition: border-color .12s, transform .12s; }
  .site:hover { border-color: var(--accent); text-decoration: none; }
  .site:hover .name { color: var(--accent-ink); }
  .site .row { display: flex; align-items: center; gap: 10px; min-width: 0; }
  .site .ico { width: 34px; height: 34px; border-radius: 8px; display: grid; place-items: center; background: var(--surface); color: var(--muted); font-weight: 700; font-size: 13px; flex: none; }
  .site .name { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .site .url { color: var(--muted); font-family: var(--mono); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .tags { display: flex; gap: 6px; flex-wrap: wrap; }
  .tag { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 6px; background: var(--surface); color: var(--muted); }
  .tag.warn { background: var(--amber-soft); color: var(--amber); }
  .site.bad { border-style: dashed; }

  .list .item { display: flex; align-items: center; gap: 12px; padding: 12px 16px; border-top: 1px solid var(--line); flex-wrap: wrap; }
  .list .item:first-child { border-top: 0; }
  .list .grow { flex: 1; min-width: 200px; }

  .empty { padding: 28px; text-align: center; color: var(--muted); }
  .empty b { display: block; color: var(--ink); font-size: 16px; margin-bottom: 4px; }
  .steps { display: inline-grid; text-align: left; gap: 6px; margin-top: 12px; counter-reset: s; }
  .steps div::before { counter-increment: s; content: counter(s); display: inline-grid; place-items: center; width: 22px; height: 22px; border-radius: 50%; background: var(--accent-soft); color: var(--accent-ink); font-weight: 700; font-size: 12px; margin-right: 10px; }

  .exts { display: flex; gap: 6px; flex-wrap: wrap; padding: 14px 16px; }
  .ext { font-family: var(--mono); font-size: 12px; padding: 3px 8px; border-radius: 6px; border: 1px solid var(--line); }
  .ext.on { color: var(--green); background: var(--green-soft); border-color: transparent; }
  .ext.off { color: var(--faint); text-decoration: line-through; }

  .lan { padding: 14px 16px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; background: var(--accent-soft); border-color: transparent; }
  footer { color: var(--faint); font-size: 12px; display: flex; gap: 16px; flex-wrap: wrap; justify-content: space-between; border-top: 1px solid var(--line); padding-top: 16px; }
  @media (max-width: 560px) { .wrap { padding: 20px 16px 40px; } .title h1 { font-size: 24px; } .search { width: 100%; } }
</style>
</head>
<body>
<div class="wrap">
  <header>
    <svg class="logo" viewBox="0 0 64 64" aria-hidden="true"><rect width="64" height="64" rx="16" fill="#EA4B0C"/><path d="M33 12c2 9 13 14 13 27a14 14 0 0 1-28 0c0-7 4-11 7-14 0 5 2 8 5 9-2-8 1-15 3-22z" fill="#fff"/></svg>
    <div class="title">
      <h1>WebHut</h1>
      <p>Your local server is running · <span class="mono"><?= h(str_replace('/', '\\', (string)$root)) ?></span></p>
    </div>
    <span class="chip <?= $allUp ? 'up' : 'warn' ?>"><?= $allUp ? 'Apache and MariaDB running' : ($dbState === 'warn' ? 'Apache running · MariaDB status unknown' : 'Apache running · MariaDB stopped') ?></span>
  </header>

  <div class="stats">
    <div class="card stat">
      <div class="k">Apache <span class="dot up"></span></div>
      <div class="v"><?= h($apacheVersion !== '' ? $apacheVersion : 'Running') ?></div>
      <div class="s">Port <span class="mono"><?= $port ?></span><?= $lanOn ? ' · network access on' : ' · this PC only' ?></div>
    </div>
    <div class="card stat">
      <div class="k">PHP <span class="dot up"></span></div>
      <div class="v"><?= h(PHP_VERSION) ?></div>
      <div class="s"><?= h($sapi) ?> · <?= count($exts) ?> extensions · <a href="/?phpinfo=1">phpinfo()</a></div>
    </div>
    <div class="card stat">
      <div class="k">MariaDB <span class="dot <?= $dbState ?>"></span></div>
      <div class="v"><?= h($dbVersion !== '' ? $dbVersion : ($dbState === 'up' ? 'Running' : 'Stopped')) ?></div>
      <div class="s"><span class="mono">127.0.0.1:<?= $dbPort ?></span> · <?= h($dbText) ?></div>
    </div>
    <div class="card stat">
      <div class="k">Projects</div>
      <div class="v"><?= count($sites) ?> <span style="font-size:14px;font-weight:400;color:var(--muted)">site<?= count($sites) === 1 ? '' : 's' ?></span></div>
      <div class="s"><?= count($nodeApps) ?> Node app<?= count($nodeApps) === 1 ? '' : 's' ?><?php $up = count(array_filter($nodeApps, fn($a) => $a['up'])); if ($nodeApps): ?> · <?= $up ?> running<?php endif; ?></div>
    </div>
  </div>

  <div class="actions">
    <a class="btn primary" href="/phpmyadmin/"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/></svg>phpMyAdmin</a>
    <a class="btn" href="/?phpinfo=1"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/></svg>PHP info</a>
    <a class="btn" href="https://github.com/hashencoder/webhut#readme" target="_blank" rel="noopener"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 5a2 2 0 0 1 2-2h12v18H6a2 2 0 0 1-2-2z"/><path d="M8 7h6"/></svg>Documentation</a>
  </div>

  <?php if ($lanOn): ?>
  <section>
    <div class="card lan">
      <?php if ($lanIps): $ip = $lanIps[0]; ?>
        <strong>On your network</strong>
        <span>Phones and PCs on the same Wi-Fi can open <a class="mono" href="http://<?= h($ip . $suffix) ?>/">http://<?= h($ip . $suffix) ?>/</a></span>
        <span class="s" style="color:var(--muted)">Each site: <code>http://&lt;site&gt;.<?= h($ip) ?>.nip.io<?= h($suffix) ?></code> or <code>http://<?= h($ip . $suffix) ?>/&lt;site&gt;/</code></span>
      <?php else: ?>
        <strong>Network access is on</strong>
        <span>but this PC isn't connected to a private network (Wi-Fi or LAN) right now.</span>
      <?php endif; ?>
    </div>
  </section>
  <?php endif; ?>

  <section>
    <div class="sec-head">
      <h2>Sites <small>folders in www\ · <?= $validSites ?> with a .localhost address</small></h2>
      <?php if (count($sites) > 6): ?><input class="search" id="q" type="search" placeholder="Filter sites…" aria-label="Filter sites"><?php endif; ?>
    </div>
    <?php if (!$sites): ?>
      <div class="card empty">
        <b>No sites yet</b>
        Each folder in <code>www\</code> becomes its own site.
        <div class="steps">
          <div>Create a folder, e.g. <code><?= h(str_replace('/', '\\', (string)$root)) ?>\www\blog</code></div>
          <div>Click <strong>Refresh sites</strong> in WebHut</div>
          <div>Open <a class="mono" href="http://blog.localhost<?= h($suffix) ?>/">http://blog.localhost<?= h($suffix) ?></a></div>
        </div>
      </div>
    <?php else: ?>
      <div class="sites" id="sites">
      <?php foreach ($sites as $s):
          $url = $s['valid'] ? 'http://' . $s['name'] . '.localhost' . $suffix . '/' : '/' . rawurlencode($s['name']) . '/';
          $initials = strtoupper(substr(preg_replace('/[^a-z0-9]/i', '', $s['name']) ?: '?', 0, 2)); ?>
        <a class="card site<?= $s['valid'] ? '' : ' bad' ?>" href="<?= h($url) ?>" data-name="<?= h(strtolower($s['name'] . ' ' . $s['kind'])) ?>">
          <div class="row">
            <div class="ico"><?= h($initials) ?></div>
            <div style="min-width:0">
              <div class="name"><?= h($s['name']) ?></div>
              <div class="url"><?= h($s['valid'] ? $s['name'] . '.localhost' . $suffix : 'localhost' . $suffix . '/' . $s['name'] . '/') ?></div>
            </div>
          </div>
          <div class="tags">
            <span class="tag"><?= h($s['kind']) ?></span>
            <?php if ($s['public']): ?><span class="tag">serves public\</span><?php endif; ?>
            <?php if (!$s['valid']): ?><span class="tag warn" title="Use lowercase letters, digits and hyphens to get name.localhost">rename for a .localhost URL</span><?php endif; ?>
          </div>
        </a>
      <?php endforeach; ?>
      </div>
      <div class="card empty" id="nomatch" hidden>No sites match.</div>
    <?php endif; ?>
  </section>

  <section>
    <div class="sec-head"><h2>Node apps <small>proxied to their own .localhost address</small></h2></div>
    <?php if (!$nodeApps): ?>
      <div class="card empty"><b>No Node apps</b>Add one in WebHut → <strong>Node apps</strong>: pick a folder and a command such as <code>npm run dev</code>.</div>
    <?php else: ?>
      <div class="card list">
      <?php foreach ($nodeApps as $app): ?>
        <div class="item">
          <span class="chip <?= $app['up'] ? 'up' : 'down' ?>" style="padding:3px 10px;font-size:12px"><?= $app['up'] ? 'Running' : 'Stopped' ?></span>
          <div class="grow">
            <a href="http://<?= h($app['name']) ?>.localhost<?= h($suffix) ?>/"><strong><?= h($app['name']) ?>.localhost</strong></a>
            <div class="mono" style="color:var(--muted)">port <?= $app['port'] ?> · <?= h($app['command']) ?></div>
          </div>
        </div>
      <?php endforeach; ?>
      </div>
    <?php endif; ?>
  </section>

  <section>
    <div class="sec-head"><h2>PHP extensions <small>common ones, as Apache's PHP has them</small></h2></div>
    <div class="card exts">
      <?php foreach ($keyExts as $e): $on = in_array($e, $exts, true); ?>
        <span class="ext <?= $on ? 'on' : 'off' ?>" title="<?= $on ? 'enabled' : 'not enabled (php.ini)' ?>"><?= h($e === 'zend opcache' ? 'opcache' : $e) ?></span>
      <?php endforeach; ?>
    </div>
  </section>

  <footer>
    <span>WebHut · Apache <?= h($apacheVersion) ?> · PHP <?= h(PHP_VERSION) ?><?= $dbVersion !== '' ? ' · MariaDB ' . h($dbVersion) : '' ?></span>
    <span>This page is <code>www\index.php</code> — replace it with your own any time.</span>
  </footer>
</div>
<script>
  (function () {
    var q = document.getElementById('q');
    if (!q) return;
    var cards = Array.prototype.slice.call(document.querySelectorAll('#sites .site'));
    var none = document.getElementById('nomatch');
    q.addEventListener('input', function () {
      var t = q.value.trim().toLowerCase(), shown = 0;
      cards.forEach(function (c) { var ok = !t || c.getAttribute('data-name').indexOf(t) >= 0; c.hidden = !ok; if (ok) shown++; });
      none.hidden = shown > 0;
    });
  })();
</script>
</body>
</html>
