<?php
/**
 * Kiln dashboard - shown at http://localhost
 * Lists your sites (folders in www\) and Node apps. Safe to replace.
 */
declare(strict_types=1);

if (isset($_GET['phpinfo'])) {
    phpinfo();
    exit;
}

$root = $_SERVER['KILN_ROOT'] ?? dirname(__DIR__);
$port = (int)($_SERVER['SERVER_PORT'] ?? 80);
$suffix = $port === 80 ? '' : ':' . $port;

function h(string $s): string
{
    return htmlspecialchars($s, ENT_QUOTES, 'UTF-8');
}

$sites = [];
foreach (scandir(__DIR__) ?: [] as $entry) {
    if ($entry[0] === '.' || !is_dir(__DIR__ . '/' . $entry)) {
        continue;
    }
    $valid = (bool)preg_match('/^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/', $entry);
    $sites[] = [
        'name'   => $entry,
        'valid'  => $valid,
        'public' => is_dir(__DIR__ . '/' . $entry . '/public'),
    ];
}

$nodeApps = [];
$appsFile = $root . '/node-apps.json';
if (is_file($appsFile)) {
    $data = json_decode((string)file_get_contents($appsFile), true);
    $nodeApps = is_array($data['apps'] ?? null) ? $data['apps'] : [];
}

$db = 'not reachable';
try {
    $mysqli = @new mysqli('127.0.0.1', 'root', '', '', (int)($_SERVER['KILN_MYSQL_PORT'] ?? 3306));
    $db = 'MariaDB ' . $mysqli->server_info;
    $mysqli->close();
} catch (Throwable $e) {
    $db = str_contains($e->getMessage(), 'Access denied')
        ? 'running (root has a password)'
        : 'not running';
}
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kiln</title>
<style>
  :root { --bg:#faf8f5; --card:#fff; --ink:#1f1d1a; --muted:#6b655d; --line:#e7e1d8; --accent:#c2410c; }
  @media (prefers-color-scheme: dark) {
    :root { --bg:#151412; --card:#1e1c19; --ink:#f1ede6; --muted:#a39b90; --line:#2f2b26; --accent:#fb923c; }
  }
  * { box-sizing: border-box; }
  body { margin:0; font:15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; background:var(--bg); color:var(--ink); }
  main { max-width: 880px; margin: 0 auto; padding: 40px 16px; }
  h1 { font-size: 28px; margin: 0 0 4px; }
  h1 span { color: var(--accent); }
  .sub { color: var(--muted); margin: 0 0 32px; }
  .grid { display:grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-bottom: 32px; }
  .stat { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px 16px; }
  .stat b { display:block; font-size:12px; text-transform:uppercase; letter-spacing:.05em; color:var(--muted); font-weight:600; }
  h2 { font-size: 16px; margin: 28px 0 10px; }
  ul { list-style:none; padding:0; margin:0; background:var(--card); border:1px solid var(--line); border-radius:10px; }
  li { padding: 12px 16px; border-top: 1px solid var(--line); display:flex; justify-content:space-between; gap:12px; flex-wrap:wrap; }
  li:first-child { border-top: 0; }
  a { color: var(--accent); text-decoration: none; }
  a:hover { text-decoration: underline; }
  .muted { color: var(--muted); font-size: 13px; }
  code { font: 13px ui-monospace, Consolas, monospace; }
</style>
</head>
<body>
<main>
  <h1><span>Kiln</span> is running</h1>
  <p class="sub">Local development stack for Windows</p>

  <div class="grid">
    <div class="stat"><b>PHP</b><?= h(PHP_VERSION) ?> · <a href="/?phpinfo=1">phpinfo</a></div>
    <div class="stat"><b>Apache</b><?= h(explode(' ', $_SERVER['SERVER_SOFTWARE'] ?? 'Apache')[0]) ?></div>
    <div class="stat"><b>Database</b><?= h($db) ?></div>
    <div class="stat"><b>Tools</b><a href="/phpmyadmin/">phpMyAdmin</a></div>
  </div>

  <h2>Sites in www\</h2>
  <?php if (!$sites): ?>
    <p class="muted">No sites yet. Create a folder such as <code>www\blog</code>, click "Refresh sites" in Kiln, then open <code>http://blog.localhost<?= h($suffix) ?></code>.</p>
  <?php else: ?>
    <ul>
    <?php foreach ($sites as $s): ?>
      <li>
        <?php if ($s['valid']): ?>
          <a href="http://<?= h($s['name']) ?>.localhost<?= h($suffix) ?>/"><?= h($s['name']) ?>.localhost</a>
          <span class="muted"><?= $s['public'] ? 'serves public\\' : 'serves folder root' ?></span>
        <?php else: ?>
          <span><?= h($s['name']) ?></span>
          <span class="muted">Rename to lowercase letters, digits and hyphens to get a URL · <a href="/<?= h(rawurlencode($s['name'])) ?>/">open as /<?= h($s['name']) ?>/</a></span>
        <?php endif; ?>
      </li>
    <?php endforeach; ?>
    </ul>
  <?php endif; ?>

  <h2>Node apps</h2>
  <?php if (!$nodeApps): ?>
    <p class="muted">No Node apps registered. Add one from Kiln → Node apps.</p>
  <?php else: ?>
    <ul>
    <?php foreach ($nodeApps as $app): $name = (string)($app['name'] ?? ''); ?>
      <li>
        <a href="http://<?= h($name) ?>.localhost<?= h($suffix) ?>/"><?= h($name) ?>.localhost</a>
        <span class="muted">port <?= (int)($app['port'] ?? 0) ?> · <code><?= h((string)($app['command'] ?? '')) ?></code></span>
      </li>
    <?php endforeach; ?>
    </ul>
  <?php endif; ?>
</main>
</body>
</html>
