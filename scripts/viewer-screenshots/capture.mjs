// Smoke + screenshot capture for the `unilyze serve` viewer.
//
// Renders the live viewer in headless Chromium (Playwright), fails the build on any
// console/page error or missing key element, and writes screenshots of each screen to
// ./out for human review (uploaded as a CI artifact). This is a smoke/visual gate, not a
// pixel-diff regression — it catches "the viewer is broken", not cosmetic drift.
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, mkdirSync, rmSync, symlinkSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..', '..');
const outDir = join(here, 'out');
const failures = [];

function fail(msg) { failures.push(msg); console.error('FAIL: ' + msg); }

function resolveDll() {
  if (process.env.UNILYZE_DLL) return process.env.UNILYZE_DLL;
  for (const cfg of ['Release', 'Debug']) {
    for (const tfm of ['net10.0', 'net9.0', 'net8.0']) {
      const p = join(repoRoot, 'src', 'Unilyze', 'bin', cfg, tfm, 'Unilyze.dll');
      if (existsSync(p)) return p;
    }
  }
  throw new Error('Unilyze.dll not found; set UNILYZE_DLL or build src/Unilyze first.');
}

function makeSampleProject() {
  const dir = mkdtempSync(join(tmpdir(), 'unilyze-shot-'));
  mkdirSync(join(dir, 'Domain'), { recursive: true });
  mkdirSync(join(dir, 'App'), { recursive: true });
  writeFileSync(join(dir, 'Domain', 'Entities.cs'), `namespace Shop.Domain;
public interface IRepository<T> { T? Get(int id); }
public class Product { public int Id; public string Name = ""; public Money Price = new(); }
public class Money { public decimal Amount; public string Currency = "USD"; }
public class Order { public Product[] Items = System.Array.Empty<Product>(); public Money Total() => new(); }
public class ProductRepository : IRepository<Product> { public Product? Get(int id) => new(); }
`);
  writeFileSync(join(dir, 'App', 'Services.cs'), `namespace Shop.App;
using Shop.Domain;
public class CartService {
  readonly IRepository<Product> _repo;
  public CartService(IRepository<Product> repo){ _repo = repo; }
  public Order Checkout(int[] ids){ var o = new Order(); foreach(var id in ids){ var p = _repo.Get(id); } return o; }
}
public class PricingService { public Money Apply(Order o) => o.Total(); }
`);
  return dir;
}

function startServe(dll, projectDir) {
  return new Promise((resolvePromise, reject) => {
    const proc = spawn('dotnet', [dll, 'serve', '-p', projectDir, '--no-open'], { stdio: ['ignore', 'ignore', 'pipe'] });
    let buf = '';
    const timer = setTimeout(() => reject(new Error('serve did not print a URL within 60s')), 60000);
    proc.stderr.on('data', (d) => {
      buf += d.toString();
      const m = buf.match(/listening on (http:\/\/127\.0\.0\.1:\d+\/)/);
      if (m) { clearTimeout(timer); resolvePromise({ proc, url: m[1] }); }
    });
    proc.on('exit', (code) => { clearTimeout(timer); reject(new Error('serve exited early (' + code + ')')); });
  });
}

async function main() {
  rmSync(outDir, { recursive: true, force: true });
  mkdirSync(outDir, { recursive: true });

  const dll = resolveDll();
  const projectDir = makeSampleProject();
  const { proc, url } = await startServe(dll, projectDir);
  console.log('serve URL:', url);

  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
  const consoleErrors = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));

  try {
    // Not networkidle: the viewer holds an open long-poll, so the network is never idle.
    await page.goto(url, { waitUntil: 'domcontentloaded' });

    // (1) Live initial view — wait for the first snapshot to render badges.
    await page.waitForSelector('.hb', { timeout: 30000 }).catch(() => fail('no health badges rendered'));
    await page.waitForTimeout(1500);
    const status = (await page.textContent('#ssText').catch(() => '')) || '';
    if (!/live/.test(status)) fail(`status bar not "live" (was "${status}")`);
    const stats = (await page.textContent('#st').catch(() => '')) || '';
    if (!/\d+ types/.test(stats)) fail(`stats bar missing type count (was "${stats}")`);
    await page.screenshot({ path: join(outDir, '01-initial.png') });

    // (2) Expanded type-dependency graph.
    await page.click('#bExp');
    await page.waitForTimeout(3000);
    await page.click('#bFit').catch(() => {});
    await page.waitForTimeout(1200);
    await page.screenshot({ path: join(outDir, '02-graph.png') });

    // (3) Type detail panel -> in-browser read-only source view.
    await page.evaluate(() => {
      const cy = window.unilyzeCy;
      const t = cy && cy.nodes('[nodeType="type"]').filter((n) => n.style('display') !== 'none')[0];
      if (t) t.emit('tap');
    });
    await page.waitForSelector('#dp:not(.hidden)', { timeout: 5000 }).catch(() => fail('detail panel did not open'));
    const srcBtn = await page.$('.src-btn');
    if (!srcBtn) {
      fail('no "View source" button in detail panel');
    } else {
      await srcBtn.click();
      await page.waitForSelector('#sp:not(.hidden)', { timeout: 8000 }).catch(() => fail('source panel did not open'));
      const body = (await page.textContent('#spBody').catch(() => '')) || '';
      if (body.length < 5) fail('source panel body empty');
      await page.screenshot({ path: join(outDir, '03-source.png') });
    }

    // (4) Live edit -> focus the changed block. Editing a source file triggers a re-analysis;
    // the next snapshot reports the changed fileId and the viewer pans/highlights its types.
    writeFileSync(join(projectDir, 'Domain', 'Entities.cs'), `namespace Shop.Domain;
public interface IRepository<T> { T? Get(int id); }
public class Product { public int Id; public string Name = ""; public Money Price = new(); }
public class Money { public decimal Amount; public string Currency = "USD"; public decimal Tax; public decimal WithTax() => Amount + Tax; }
public class Order { public Product[] Items = System.Array.Empty<Product>(); public Money Total() => new(); }
public class ProductRepository : IRepository<Product> { public Product? Get(int id) => new(); }
`);
    await page.waitForFunction(
      () => window.unilyzeCy && window.unilyzeCy.elements('.hl-changed').length > 0,
      { timeout: 30000 }
    ).catch(() => fail('changed block was not highlighted after a live edit'));
    await page.waitForTimeout(400);
    await page.screenshot({ path: join(outDir, '04-edit-focus.png') });

    // (5) Stale state: a genuine analysis failure must keep the prior snapshot + show the banner.
    try { symlinkSync(join(tmpdir(), 'unilyze-missing-' + Date.now() + '.cs'), join(projectDir, 'Broken.cs')); } catch { /* ignore */ }
    await page.waitForSelector('#staleBanner:not(.hidden)', { timeout: 30000 })
      .catch(() => fail('stale banner did not appear after analysis failure'));
    await page.waitForTimeout(500);
    await page.screenshot({ path: join(outDir, '05-stale.png') });

    if (consoleErrors.length) fail('console/page errors: ' + consoleErrors.join(' | '));
  } finally {
    await browser.close().catch(() => {});
    try { proc.kill('SIGINT'); } catch { /* ignore */ }
    rmSync(projectDir, { recursive: true, force: true });
  }

  if (failures.length) {
    console.error(`\n${failures.length} screenshot-smoke failure(s).`);
    process.exit(1);
  }
  console.log('\nViewer smoke OK; screenshots in', outDir);
}

main().catch((e) => { console.error(e); process.exit(1); });
