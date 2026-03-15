namespace Unilyze;

internal static class HtmlTemplate
{
    internal const string Value = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__ - Type Dependencies</title>
<style>
*,*::before,*::after{margin:0;padding:0;box-sizing:border-box}
:root{
  --bg:#0b0f19;--surface:#131825;--border:#1e2538;
  --text:#c9d1d9;--dim:#6e7681;--accent:#58a6ff;
  --font:'SFMono-Regular','Cascadia Code','Fira Code','Consolas',monospace;
}
body{
  background:radial-gradient(circle,#141b2d 1px,transparent 1px) 0 0/24px 24px,var(--bg);
  color:var(--text);font-family:var(--font);
  font-variation-settings:'MONO' 0,'CASL' 0;
  height:100vh;display:flex;flex-direction:column;overflow:hidden;
}
.toolbar{
  display:flex;align-items:center;gap:10px;padding:7px 14px;
  background:var(--surface);border-bottom:1px solid var(--border);z-index:10;
}
.toolbar h1{
  font-size:13px;font-weight:600;color:var(--accent);white-space:nowrap;
  font-variation-settings:'MONO' 1,'CASL' 0;letter-spacing:.4px;
}
.sep{width:1px;height:18px;background:var(--border)}
.search{
  background:var(--bg);border:1px solid var(--border);border-radius:5px;
  padding:4px 10px;color:var(--text);font-family:var(--font);
  font-variation-settings:'MONO' 1,'CASL' 0;font-size:12px;width:220px;outline:none;
  transition:border-color .2s;
}
.search:focus{border-color:var(--accent)}
.search::placeholder{color:var(--dim)}
.btn{
  background:var(--bg);border:1px solid var(--border);border-radius:5px;
  padding:4px 10px;color:var(--dim);font-family:var(--font);font-size:11px;
  cursor:pointer;transition:all .15s;white-space:nowrap;
}
.btn:hover{color:var(--text);border-color:var(--dim)}
.spacer{flex:1}
.stats{font-size:11px;color:var(--dim);font-variation-settings:'MONO' 1,'CASL' 0}
.main{flex:1;display:flex;position:relative;overflow:hidden}
#cy{flex:1}
.panel{
  width:300px;background:var(--surface);border-left:1px solid var(--border);
  overflow-y:auto;padding:14px;font-size:12px;
  position:absolute;right:0;top:0;bottom:0;z-index:5;
  transition:transform .2s ease,opacity .2s ease;
}
.panel.hidden{transform:translateX(100%);opacity:0;pointer-events:none}
.panel h2{
  font-size:13px;font-weight:600;font-variation-settings:'MONO' 1,'CASL' 0;
  margin-bottom:3px;word-break:break-all;
}
.panel .meta{color:var(--dim);font-size:11px;margin-bottom:10px;font-variation-settings:'MONO' 1,'CASL' 0}
.section-title{
  font-size:10px;text-transform:uppercase;letter-spacing:.8px;color:var(--dim);
  margin:10px 0 5px;padding-bottom:3px;border-bottom:1px solid var(--border);
}
.member{
  padding:2px 0;font-variation-settings:'MONO' 1,'CASL' 0;font-size:11px;
  display:flex;gap:5px;align-items:baseline;
}
.badge{
  font-size:9px;padding:1px 4px;border-radius:3px;
  background:var(--bg);color:var(--dim);flex-shrink:0;
}
.member .n{color:var(--text)}.member .t{color:var(--accent)}
.close-btn{
  position:absolute;top:6px;right:6px;background:none;border:none;
  color:var(--dim);cursor:pointer;font-size:16px;padding:4px;
}
.close-btn:hover{color:var(--text)}
.legend{
  display:flex;align-items:center;gap:12px;padding:5px 14px;
  background:var(--surface);border-top:1px solid var(--border);
  font-size:10px;color:var(--dim);overflow-x:auto;flex-shrink:0;
}
.legend b{font-weight:600;color:var(--text)}
.li{display:flex;align-items:center;gap:3px;white-space:nowrap}
.sw{width:8px;height:8px;border-radius:2px;flex-shrink:0}
.el{width:14px;height:0;flex-shrink:0}
.tip{
  position:absolute;pointer-events:none;z-index:20;
  background:rgba(19,24,37,.96);border:1px solid var(--border);border-radius:6px;
  padding:10px 12px;font-size:11px;max-width:340px;
  box-shadow:0 4px 16px rgba(0,0,0,.5);
  transform:translate(12px,12px);
  opacity:0;transition:opacity .12s;
  font-variation-settings:'MONO' 1,'CASL' 0;
}
.tip.show{opacity:1}
.tip h3{font-size:12px;font-weight:600;margin-bottom:2px;word-break:break-all}
.tip .tm{color:var(--dim);font-size:10px;margin-bottom:6px}
.tip .tl{display:flex;gap:4px;align-items:baseline;padding:1px 0}
.tip .tl .tk{color:var(--dim);font-size:9px;flex-shrink:0}
.tip .tl .tn{color:var(--text)}.tip .tl .tt{color:var(--accent)}
.tip .ts{font-size:10px;color:var(--dim);margin-top:4px;padding-top:4px;border-top:1px solid var(--border)}
#badges{position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:4;overflow:hidden}
.hb{
  position:absolute;pointer-events:auto;cursor:default;
  min-width:18px;height:18px;border-radius:9px;
  display:flex;align-items:center;justify-content:center;
  font-family:var(--font);font-size:9px;font-weight:700;
  font-variation-settings:'MONO' 1,'CASL' 0;
  color:#0b0f19;padding:0 5px;
  box-shadow:0 1px 4px rgba(0,0,0,.5);
  transform-origin:center center;
  white-space:nowrap;
}
.hb:hover{filter:brightness(1.2)}
.hb-ns{font-size:11px;min-width:22px;height:22px;border-radius:11px;padding:0 6px}
.hb-cluster{min-width:28px;height:24px;border-radius:12px;padding:0 7px;gap:3px;font-size:10px}
.hb-cluster .hbc{font-size:7px;opacity:.8}
.btip{
  position:absolute;pointer-events:none;z-index:25;
  background:rgba(19,24,37,.97);border:1px solid var(--border);border-radius:6px;
  padding:8px 10px;font-size:10px;max-width:280px;
  box-shadow:0 4px 16px rgba(0,0,0,.5);
  opacity:0;transition:opacity .12s;
  font-variation-settings:'MONO' 1,'CASL' 0;color:var(--text);
}
.btip.show{opacity:1}
.btip .bh{font-size:11px;font-weight:600;margin-bottom:4px}
.btip .br{display:flex;gap:4px;align-items:baseline;padding:1px 0}
.btip .bk{color:var(--dim);font-size:9px;flex-shrink:0;min-width:32px}
.btip .bv{color:var(--text)}
.btip .bw{color:#f97583}
.edge-filter{
  position:absolute;top:42px;z-index:15;
  background:var(--surface);border:1px solid var(--border);border-radius:6px;
  padding:8px 10px;font-size:11px;box-shadow:0 4px 16px rgba(0,0,0,.5);
  display:none;
}
.edge-filter.show{display:block}
.ef-row{display:flex;align-items:center;gap:6px;padding:2px 0;cursor:pointer}
.ef-row:hover{color:var(--accent)}
.ef-cb{width:12px;height:12px;accent-color:var(--accent)}
.ef-swatch{width:10px;height:2px;flex-shrink:0}
::-webkit-scrollbar{width:5px}
::-webkit-scrollbar-track{background:var(--bg)}
::-webkit-scrollbar-thumb{background:var(--border);border-radius:3px}
.left-panel{
  width:300px;background:var(--surface);border-right:1px solid var(--border);
  overflow:hidden;font-size:12px;
  position:absolute;left:0;top:0;bottom:0;z-index:5;
  transition:transform .2s ease,opacity .2s ease;
  display:flex;flex-direction:column;
}
.left-panel.hidden{transform:translateX(-100%);opacity:0;pointer-events:none}
.lp-header{padding:10px 12px 8px;border-bottom:1px solid var(--border)}
.lp-tabs{display:flex;gap:4px;margin-bottom:8px}
.lp-tab{
  background:var(--bg);border:1px solid var(--border);border-radius:4px;
  padding:3px 10px;color:var(--dim);font-family:var(--font);font-size:11px;
  cursor:pointer;transition:all .15s;
}
.lp-tab.active{color:var(--text);border-color:var(--accent);background:rgba(88,166,255,.1)}
.lp-sort{
  background:var(--bg);border:1px solid var(--border);border-radius:4px;
  padding:3px 8px;color:var(--text);font-family:var(--font);font-size:11px;width:100%;
}
.lp-list{flex:1;overflow-y:auto;padding:4px 0}
.hp-item{
  padding:6px 12px;cursor:pointer;transition:background .1s;
  border-bottom:1px solid var(--border);
}
.hp-item:hover{background:rgba(88,166,255,.06)}
.hp-name{font-size:11px;font-variation-settings:'MONO' 1;color:var(--text);word-break:break-all}
.hp-sub{font-size:10px;color:var(--dim);margin-top:2px}
.hp-badges{display:flex;gap:4px;margin-top:3px;flex-wrap:wrap}
.hp-badges .badge{font-size:9px}
.cyc-group{padding:8px 12px;border-bottom:1px solid var(--border)}
.cyc-title{font-size:11px;font-weight:600;color:#f97583;cursor:pointer;margin-bottom:4px}
.cyc-title:hover{color:#ff9eaa}
.cyc-item{
  font-size:11px;font-variation-settings:'MONO' 1;color:var(--text);
  padding:1px 0;cursor:pointer;
}
.cyc-item:hover{color:var(--accent)}
.btn.active{color:var(--accent);border-color:var(--accent);background:rgba(88,166,255,.08)}
.btn .cnt{
  display:inline-block;min-width:14px;height:14px;border-radius:7px;
  font-size:9px;line-height:14px;text-align:center;margin-left:4px;
  background:#f97583;color:#0b0f19;font-weight:700;
}
.modal-overlay{
  position:fixed;top:0;left:0;right:0;bottom:0;
  background:rgba(11,15,25,.8);z-index:50;
  display:flex;align-items:center;justify-content:center;
}
.modal-overlay.hidden{display:none}
.modal{
  background:var(--surface);border:1px solid var(--border);border-radius:8px;
  padding:20px;max-width:90vw;max-height:85vh;overflow:auto;
  box-shadow:0 8px 32px rgba(0,0,0,.6);min-width:400px;
}
.modal h2{font-size:14px;font-weight:600;color:var(--accent);margin-bottom:12px}
.modal h3{font-size:12px;font-weight:600;color:var(--text);margin:14px 0 8px}
.asm-table{
  border-collapse:collapse;font-size:10px;
  font-variation-settings:'MONO' 1;width:100%;
}
.asm-table th,.asm-table td{
  padding:4px 6px;border:1px solid var(--border);text-align:center;
}
.asm-table th{background:var(--bg);color:var(--dim);font-weight:600;position:sticky;top:0}
.asm-table td.c0{background:transparent}
.asm-table td.c1{background:rgba(126,231,135,.1);color:#7ee787}
.asm-table td.c2{background:rgba(227,179,65,.15);color:#e3b341}
.asm-table td.c3{background:rgba(249,117,131,.15);color:#f97583}
.asm-table td.diag{background:var(--bg);color:var(--dim)}
.coup-table{
  border-collapse:collapse;font-size:11px;width:100%;
  font-variation-settings:'MONO' 1;
}
.coup-table th,.coup-table td{
  padding:5px 8px;border:1px solid var(--border);text-align:left;
}
.coup-table th{background:var(--bg);color:var(--dim);font-weight:600}
.coup-table .hi{color:#f97583}
body.offline-mode{height:auto;overflow:auto}
body.offline-mode .main{min-height:calc(100vh - 70px);overflow:visible}
body.offline-mode #cy{overflow:visible}
body.offline-mode #badges,
body.offline-mode .tip,
body.offline-mode .btip,
body.offline-mode .left-panel{display:none!important}
body.offline-mode .panel{
  position:fixed;top:58px;bottom:16px;right:16px;border:1px solid var(--border);
  border-radius:8px;box-shadow:0 8px 32px rgba(0,0,0,.45)
}
.offline-report{padding:18px;display:grid;gap:16px}
.offline-banner{
  border:1px solid rgba(227,179,65,.45);background:rgba(227,179,65,.08);
  border-radius:8px;padding:12px 14px;font-size:12px;line-height:1.6;color:var(--text)
}
.offline-banner b{color:#e3b341}
.offline-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}
.offline-card,.offline-section{
  background:var(--surface);border:1px solid var(--border);border-radius:8px;
  padding:14px
}
.offline-card .k{
  font-size:10px;text-transform:uppercase;letter-spacing:.8px;color:var(--dim);margin-bottom:6px
}
.offline-card .v{font-size:22px;font-weight:700;color:var(--text)}
.offline-card .s{margin-top:4px;font-size:11px;color:var(--dim)}
.offline-section h2{font-size:13px;font-weight:600;color:var(--accent);margin-bottom:10px}
.offline-section .sub{font-size:11px;color:var(--dim);margin-bottom:10px}
.offline-table-wrap{overflow:auto}
.offline-table{
  width:100%;border-collapse:collapse;font-size:11px;font-variation-settings:'MONO' 1
}
.offline-table th,.offline-table td{
  padding:7px 8px;border-bottom:1px solid var(--border);text-align:left;vertical-align:top
}
.offline-table th{
  position:sticky;top:0;background:var(--surface);color:var(--dim);font-weight:600;z-index:1
}
.offline-table tr:hover td{background:rgba(88,166,255,.05)}
.offline-link{
  color:var(--accent);cursor:pointer;text-decoration:none;background:none;border:none;
  padding:0;font:inherit;text-align:left
}
.offline-link:hover{text-decoration:underline}
.offline-pills{display:flex;gap:6px;flex-wrap:wrap}
.offline-pill{
  display:inline-flex;align-items:center;border:1px solid var(--border);border-radius:999px;
  padding:2px 8px;font-size:10px;color:var(--text);background:var(--bg)
}
.offline-empty{font-size:11px;color:var(--dim)}
.offline-split{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:16px}
.offline-list{display:grid;gap:8px}
.offline-item{
  border:1px solid var(--border);border-radius:6px;padding:10px 12px;background:rgba(11,15,25,.35)
}
.offline-item .t{font-size:11px;color:var(--text)}
.offline-item .m{font-size:10px;color:var(--dim);margin-top:4px}
@media (max-width: 960px){
  body.offline-mode .panel{
    position:fixed;left:12px;right:12px;top:72px;bottom:12px;width:auto
  }
}
</style>
</head>
<body>
<div class="toolbar">
  <h1>__TITLE__</h1><div class="sep"></div>
  <input type="text" class="search" placeholder="Search types..." id="q">
  <button class="btn" id="bExp">Expand All</button>
  <button class="btn" id="bCol">Collapse All</button>
  <button class="btn" id="bFit">Fit</button>
  <button class="btn" id="bLay">Re-layout</button>
  <div class="sep"></div>
  <button class="btn" id="bHot">Hotspots</button>
  <button class="btn" id="bCyc">Cycles</button>
  <button class="btn" id="bAsm">Assemblies</button>
  <div class="sep"></div>
  <select class="btn" id="edgeStyle" style="padding:3px 6px"><option value="bezier">Bezier</option><option value="taxi">Taxi</option><option value="elk" selected>ELK</option></select>
  <button class="btn active" id="bEdge">Edges</button>
  <div class="spacer"></div>
  <span class="stats" id="st"></span>
</div>
<div class="main">
  <div class="left-panel hidden" id="hp">
    <div class="lp-header">
      <div class="lp-tabs">
        <button class="lp-tab active" data-panel="hp" data-tab="methods">Methods</button>
        <button class="lp-tab" data-panel="hp" data-tab="types">Types</button>
      </div>
      <select class="lp-sort" id="hpSort">
        <option value="cogcc">Sort by CogCC</option>
        <option value="cyccc">Sort by CycCC</option>
        <option value="health">Sort by Health</option>
        <option value="loc">Sort by LOC</option>
        <option value="nest">Sort by Nesting</option>
      </select>
    </div>
    <div class="lp-list" id="hpList"></div>
  </div>
  <div class="left-panel hidden" id="cycp">
    <div class="lp-header">
      <div style="font-size:13px;font-weight:600;color:#f97583">Circular Dependencies</div>
    </div>
    <div class="lp-list" id="cycList"></div>
  </div>
  <div id="cy"></div>
  <div id="badges"></div>
  <div class="panel hidden" id="dp">
    <button class="close-btn" id="cls">&times;</button>
    <div id="dc"></div>
  </div>
</div>
<div class="legend" id="lg"></div>
<div class="tip" id="tip"></div>
<div class="btip" id="btip"></div>
<div class="edge-filter" id="efPanel"></div>
<div class="modal-overlay hidden" id="asmOv">
  <div class="modal"><div id="asmContent"></div></div>
</div>

<script src="https://unpkg.com/cytoscape@3.30.4/dist/cytoscape.min.js"></script>
<script src="https://unpkg.com/dagre@0.8.5/dist/dagre.min.js"></script>
<script src="https://unpkg.com/cytoscape-dagre@2.5.0/cytoscape-dagre.js"></script>
<script src="https://unpkg.com/elkjs@0.9.3/lib/elk.bundled.js"></script>
<script src="https://unpkg.com/cytoscape-elk@2.3.0/dist/cytoscape-elk.js"></script>
<script>
const DATA = __DATA_PLACEHOLDER__;

function stripGenericArgs(name){
  const v=(name||'').replace(/^global::/,'').replace(/\?$/,'');
  const i=v.indexOf('<');
  return i>=0 ? v.slice(0,i) : v;
}
function qualifiedName(ns,name){
  const simple=stripGenericArgs(name);
  return ns ? ns+'.'+simple : simple;
}
function typeKey(t){ return t.typeId || qualifiedName(t.namespace, t.name); }
function metricKey(m){ return m.typeId || qualifiedName(m.namespace, m.typeName); }
function depFromId(d){ return d.fromTypeId || d.fromType; }
function depToId(d){ return d.toTypeId || d.toType; }

const PAL = [
  '#58a6ff','#7ee787','#d2a8ff','#ffa657','#f97583',
  '#79c0ff','#ffd700','#f778ba','#56d364','#bc8cff','#e3b341','#a5d6ff'
];
const DC = {
  Inheritance:'#f97583',InterfaceImpl:'#79c0ff',FieldType:'#7ee787',
  PropertyType:'#d2a8ff',MethodParam:'#ffa657',ReturnType:'#ff7b72',
  ConstructorParam:'#e3b341',EventType:'#f778ba',GenericConstraint:'#a5d6ff'
};
const DS = {
  Inheritance:{s:'solid',w:2.5,a:'triangle'},
  InterfaceImpl:{s:'dashed',w:2,a:'triangle'},
  FieldType:{s:'solid',w:1,a:'vee'},PropertyType:{s:'solid',w:1,a:'vee'},
  MethodParam:{s:'dotted',w:1,a:'vee'},ReturnType:{s:'solid',w:1,a:'vee'},
  ConstructorParam:{s:'dotted',w:1.5,a:'vee'},EventType:{s:'dashed',w:1,a:'vee'},
  GenericConstraint:{s:'dotted',w:1,a:'vee'}
};
const KS = {
  'class':'round-rectangle','record':'round-rectangle','record struct':'round-rectangle',
  'struct':'rectangle','interface':'diamond','enum':'hexagon',
  'delegate':'ellipse','type':'round-rectangle'
};

// Assembly color map
const asm = DATA.assemblies||[];
const ac = {};
asm.forEach((a,i)=>{ac[a.name]=PAL[i%PAL.length]});

// Type lookup
const tl = {};
DATA.types.forEach(t=>{tl[typeKey(t)]=t});

// TypeMetrics lookup
const tm = {};
(DATA.typeMetrics||[]).forEach(m=>{tm[metricKey(m)]=m});

function healthColor(score){
  if(score==null) return null;
  if(score>=9) return '#56d364';
  if(score>=7) return '#7ee787';
  if(score>=4) return '#e3b341';
  if(score>=2) return '#f97583';
  return '#ff7b72';
}

function escapeHtml(value){
  return String(value)
    .replace(/&/g,'&amp;')
    .replace(/</g,'&lt;')
    .replace(/>/g,'&gt;')
    .replace(/"/g,'&quot;');
}

function renderOfflineReport(){
  document.body.classList.add('offline-mode');

  const graphEl=document.getElementById('cy');
  const searchEl=document.getElementById('q');
  const statsEl=document.getElementById('st');
  const legendEl=document.getElementById('lg');
  const panelEl=document.getElementById('dp');
  const panelContentEl=document.getElementById('dc');
  const closeBtn=document.getElementById('cls');

  ['bExp','bCol','bFit','bLay'].forEach(id=>{
    const el=document.getElementById(id);
    if(el) el.style.display='none';
  });

  const typeStats=new Map();
  DATA.types.forEach(t=>{
    typeStats.set(typeKey(t),{outgoing:0,incoming:0});
  });
  (DATA.dependencies||[]).forEach(dep=>{
    const fromId=depFromId(dep),toId=depToId(dep);
    if(typeStats.has(fromId)) typeStats.get(fromId).outgoing++;
    if(typeStats.has(toId)) typeStats.get(toId).incoming++;
  });

  const typeRows=DATA.types.map(t=>{
    const id=typeKey(t);
    const metrics=tm[id];
    const stats=typeStats.get(id)||{outgoing:0,incoming:0};
    return {
      id,
      name:t.name,
      namespace:t.namespace||'(global)',
      qualifiedName:t.qualifiedName||qualifiedName(t.namespace,t.name),
      assembly:t.assembly,
      kind:t.kind,
      health:metrics?.codeHealth??null,
      maxCogCC:metrics?.maxCognitiveComplexity??0,
      cbo:metrics?.cbo??null,
      dit:metrics?.dit??null,
      smellCount:(metrics?.codeSmells||[]).length,
      outgoing:stats.outgoing,
      incoming:stats.incoming
    };
  }).sort((a,b)=>{
    const ah=a.health??999,bh=b.health??999;
    if(ah!==bh) return ah-bh;
    return a.qualifiedName.localeCompare(b.qualifiedName);
  });

  const methodHotspots=[];
  (DATA.typeMetrics||[]).forEach(typeMetric=>{
    (typeMetric.methods||[]).forEach(method=>{
      methodHotspots.push({
        typeId:metricKey(typeMetric),
        methodName:method.name,
        typeName:typeMetric.typeName,
        qualifiedName:typeMetric.qualifiedName||qualifiedName(typeMetric.namespace,typeMetric.typeName),
        cogcc:method.cognitiveComplexity||0,
        cyccc:method.cyclomaticComplexity||0,
        loc:method.lineCount||0,
        nest:method.maxNestingDepth||0
      });
    });
  });
  methodHotspots.sort((a,b)=>
    (b.cogcc-a.cogcc)||
    (b.cyccc-a.cyccc)||
    (b.loc-a.loc)||
    a.qualifiedName.localeCompare(b.qualifiedName));

  const typeHotspots=(DATA.typeMetrics||[]).map(typeMetric=>({
    typeId:metricKey(typeMetric),
    typeName:typeMetric.typeName,
    qualifiedName:typeMetric.qualifiedName||qualifiedName(typeMetric.namespace,typeMetric.typeName),
    health:typeMetric.codeHealth??null,
    cogcc:typeMetric.maxCognitiveComplexity||0,
    cyccc:typeMetric.maxCyclomaticComplexity||0,
    loc:typeMetric.lineCount||0,
    nest:typeMetric.maxNestingDepth||0,
    smells:(typeMetric.codeSmells||[]).length
  })).sort((a,b)=>{
    const ah=a.health??999,bh=b.health??999;
    if(ah!==bh) return ah-bh;
    return (b.cogcc-a.cogcc)||a.qualifiedName.localeCompare(b.qualifiedName);
  });

  function buildCycles(){
    const adj=new Map();
    DATA.types.forEach(t=>adj.set(typeKey(t),[]));
    (DATA.dependencies||[]).forEach(dep=>{
      const fromId=depFromId(dep),toId=depToId(dep);
      if(adj.has(fromId)&&adj.has(toId)) adj.get(fromId).push(toId);
    });

    let index=0;
    const stack=[];
    const onStack=new Set();
    const indexMap=new Map();
    const lowMap=new Map();
    const found=[];

    function strongConnect(nodeId){
      indexMap.set(nodeId,index);
      lowMap.set(nodeId,index);
      index++;
      stack.push(nodeId);
      onStack.add(nodeId);

      (adj.get(nodeId)||[]).forEach(nextId=>{
        if(!indexMap.has(nextId)){
          strongConnect(nextId);
          lowMap.set(nodeId,Math.min(lowMap.get(nodeId),lowMap.get(nextId)));
        }
        else if(onStack.has(nextId)){
          lowMap.set(nodeId,Math.min(lowMap.get(nodeId),indexMap.get(nextId)));
        }
      });

      if(lowMap.get(nodeId)===indexMap.get(nodeId)){
        const component=[];
        let currentId=null;
        do{
          currentId=stack.pop();
          onStack.delete(currentId);
          component.push(currentId);
        } while(currentId!==nodeId);
        if(component.length>1)
          found.push(component.sort((a,b)=>(tl[a]?.name||a).localeCompare(tl[b]?.name||b)));
      }
    }

    adj.forEach((_,nodeId)=>{
      if(!indexMap.has(nodeId))
        strongConnect(nodeId);
    });

    return found.sort((a,b)=>b.length-a.length);
  }

  function buildAssemblyData(){
    const names=[...new Set(DATA.types.map(t=>t.assembly))].sort((a,b)=>a.localeCompare(b));
    const matrix=new Map();
    names.forEach(from=>{
      const row=new Map();
      names.forEach(to=>row.set(to,0));
      matrix.set(from,row);
    });

    (DATA.dependencies||[]).forEach(dep=>{
      const fromType=tl[depFromId(dep)];
      const toType=tl[depToId(dep)];
      if(!fromType||!toType||fromType.assembly===toType.assembly) return;
      matrix.get(fromType.assembly).set(
        toType.assembly,
        matrix.get(fromType.assembly).get(toType.assembly)+1);
    });

    const coupling=names.map(name=>{
      let ca=0,ce=0;
      names.forEach(other=>{
        if(other===name) return;
        if(matrix.get(other).get(name)>0) ca++;
        if(matrix.get(name).get(other)>0) ce++;
      });
      return {name,ca,ce,instability:ca+ce>0?ce/(ca+ce):0};
    }).sort((a,b)=>b.instability-a.instability);

    return {names,matrix,coupling};
  }

  const cycles=buildCycles();
  const assemblyData=buildAssemblyData();
  const avgHealthValues=(DATA.typeMetrics||[]).map(m=>m.codeHealth).filter(v=>v!=null);
  const avgHealth=avgHealthValues.length
    ? (avgHealthValues.reduce((sum,value)=>sum+value,0)/avgHealthValues.length).toFixed(1)
    : null;

  function renderTypeDetail(typeId){
    const type=tl[typeId];
    if(!type) return;

    const metrics=tm[typeId];
    const outgoing=(DATA.dependencies||[]).filter(dep=>depFromId(dep)===typeId);
    const incoming=(DATA.dependencies||[]).filter(dep=>depToId(dep)===typeId);
    const accent=ac[type.assembly]||'#6e7681';

    let html='<h2 style="color:'+accent+'">'+escapeHtml(type.name)+'</h2>';
    html+='<div class="meta">'+escapeHtml(type.kind)+' &middot; '+escapeHtml(type.assembly)+'</div>';
    html+='<div class="meta">'+escapeHtml(type.qualifiedName||qualifiedName(type.namespace,type.name))+'</div>';

    if(metrics){
      html+='<div class="section-title">Metrics</div>';
      html+='<div class="member"><span class="badge" style="color:'+healthColor(metrics.codeHealth)+'">Health</span><span class="n">'+metrics.codeHealth+'</span></div>';
      html+='<div class="member"><span class="badge">CogCC</span><span class="n">avg '+metrics.averageCognitiveComplexity+' / max '+metrics.maxCognitiveComplexity+'</span></div>';
      html+='<div class="member"><span class="badge">CycCC</span><span class="n">avg '+metrics.averageCyclomaticComplexity+' / max '+metrics.maxCyclomaticComplexity+'</span></div>';
      html+='<div class="member"><span class="badge">LOC</span><span class="n">'+metrics.lineCount+' lines</span></div>';
      html+='<div class="member"><span class="badge">M</span><span class="n">'+metrics.methodCount+' methods</span></div>';
      if(metrics.cbo!=null)
        html+='<div class="member"><span class="badge">CBO</span><span class="n">'+metrics.cbo+'</span></div>';
      if(metrics.dit!=null)
        html+='<div class="member"><span class="badge">DIT</span><span class="n">'+metrics.dit+'</span></div>';
      if(metrics.lcom!=null)
        html+='<div class="member"><span class="badge">LCOM</span><span class="n">'+(+metrics.lcom).toFixed(2)+'</span></div>';
      if(metrics.instability!=null)
        html+='<div class="member"><span class="badge">I</span><span class="n">'+(+metrics.instability).toFixed(2)+'</span></div>';
      if(metrics.codeSmells&&metrics.codeSmells.length){
        html+='<div class="section-title">Code Smells ('+metrics.codeSmells.length+')</div>';
        metrics.codeSmells.forEach(smell=>{
          html+='<div class="member"><span class="badge">'+escapeHtml(smell.severity[0])+'</span><span class="n">'+escapeHtml(smell.kind)+' &middot; '+escapeHtml(smell.message)+'</span></div>';
        });
      }
    }

    if(type.baseType){
      html+='<div class="section-title">Base Type</div>';
      html+='<div class="member"><span class="t">'+escapeHtml(type.baseType)+'</span></div>';
    }

    if(type.interfaces&&type.interfaces.length){
      html+='<div class="section-title">Interfaces</div>';
      type.interfaces.forEach(interfaceName=>{
        html+='<div class="member"><span class="t">'+escapeHtml(interfaceName)+'</span></div>';
      });
    }

    if(type.members&&type.members.length){
      html+='<div class="section-title">Members ('+type.members.length+')</div>';
      type.members.forEach(member=>{
        html+='<div class="member"><span class="badge">'+escapeHtml(member.memberKind[0])+'</span><span class="n">'+escapeHtml(member.name)+'</span> <span class="t">'+escapeHtml(member.type)+'</span></div>';
      });
    }

    if(outgoing.length){
      html+='<div class="section-title">Depends On ('+outgoing.length+')</div>';
      outgoing.forEach(dep=>{
        const toType=tl[depToId(dep)];
        html+='<div class="member"><span class="badge">'+escapeHtml(dep.kind)+'</span><span class="t">'+escapeHtml(toType?.qualifiedName||dep.toType)+'</span></div>';
      });
    }

    if(incoming.length){
      html+='<div class="section-title">Depended By ('+incoming.length+')</div>';
      incoming.forEach(dep=>{
        const fromType=tl[depFromId(dep)];
        html+='<div class="member"><span class="badge">'+escapeHtml(dep.kind)+'</span><span class="t">'+escapeHtml(fromType?.qualifiedName||dep.fromType)+'</span></div>';
      });
    }

    panelContentEl.innerHTML=html;
    panelEl.classList.remove('hidden');
  }

  function renderLegend(){
    let html='<b>Assemblies</b> ';
    asm.forEach(assemblyInfo=>{
      const color=ac[assemblyInfo.name]||'#6e7681';
      html+='<div class="li"><div class="sw" style="background:'+color+'"></div>'+escapeHtml(assemblyInfo.name.split('.').pop())+'</div>';
    });
    html+='<div class="sep" style="width:1px;height:14px;background:#1e2538"></div><b>Edges</b> ';
    Object.entries(DC).forEach(([kind,color])=>{
      html+='<div class="li"><div class="el" style="border-bottom:2px solid '+color+'"></div>'+escapeHtml(kind)+'</div>';
    });
    legendEl.innerHTML=html;
  }

  function metricCell(value,formatter){
    if(value==null) return '<span class="offline-empty">-</span>';
    return formatter?formatter(value):escapeHtml(value);
  }

  function render(filterText){
    const query=(filterText||'').trim().toLowerCase();
    const matches=row=>!query||[
      row.name,
      row.namespace,
      row.qualifiedName,
      row.assembly,
      row.kind
    ].some(value=>String(value).toLowerCase().includes(query));

    const filteredTypes=typeRows.filter(matches);
    const filteredDeps=(DATA.dependencies||[])
      .map(dep=>{
        const fromType=tl[depFromId(dep)];
        const toType=tl[depToId(dep)];
        return {
          dep,
          fromLabel:fromType?.qualifiedName||dep.fromType,
          toLabel:toType?.qualifiedName||dep.toType
        };
      })
      .filter(item=>!query||[
        item.fromLabel,
        item.toLabel,
        item.dep.kind
      ].some(value=>String(value).toLowerCase().includes(query)));

    const dependencyRows=query?filteredDeps:filteredDeps.slice(0,200);
    const dependencyNote=!query&&filteredDeps.length>dependencyRows.length
      ? '<div class="sub">Showing first '+dependencyRows.length+' of '+filteredDeps.length+' dependencies. Use search to narrow further.</div>'
      : '<div class="sub">'+filteredDeps.length+' matching dependencies.</div>';

    const badTypes=filteredTypes.filter(row=>row.health!=null).slice(0,10);
    const hotMethodRows=methodHotspots
      .filter(item=>!query||[
        item.methodName,
        item.typeName,
        item.qualifiedName
      ].some(value=>String(value).toLowerCase().includes(query)))
      .slice(0,10);
    const hotTypeRows=typeHotspots
      .filter(item=>!query||[
        item.typeName,
        item.qualifiedName
      ].some(value=>String(value).toLowerCase().includes(query)))
      .slice(0,10);

    graphEl.innerHTML='' +
      '<div class="offline-report">' +
        '<div class="offline-banner"><b>Offline report view</b><br>Interactive Cytoscape assets could not be loaded, so this viewer switched to a built-in report that still exposes types, dependencies, hotspots, cycles, and assembly coupling.</div>' +
        '<div class="offline-grid">' +
          '<div class="offline-card"><div class="k">Types</div><div class="v">'+DATA.types.length+'</div><div class="s">'+filteredTypes.length+' visible in current filter</div></div>' +
          '<div class="offline-card"><div class="k">Dependencies</div><div class="v">'+DATA.dependencies.length+'</div><div class="s">'+filteredDeps.length+' visible in current filter</div></div>' +
          '<div class="offline-card"><div class="k">Assemblies</div><div class="v">'+asm.length+'</div><div class="s">'+assemblyData.coupling.length+' coupling entries</div></div>' +
          '<div class="offline-card"><div class="k">Avg Health</div><div class="v">'+(avgHealth??'-')+'</div><div class="s">'+cycles.length+' cycle'+(cycles.length===1?'':'s')+'</div></div>' +
        '</div>' +
        '<div class="offline-split" id="offline-hotspots">' +
          '<section class="offline-section"><h2>Type Hotspots</h2><div class="sub">Lowest health types in current filter.</div>' +
            (hotTypeRows.length
              ? '<div class="offline-list">'+hotTypeRows.map(item=>
                  '<div class="offline-item">' +
                    '<button class="offline-link t" data-type-id="'+escapeHtml(item.typeId)+'">'+escapeHtml(item.qualifiedName)+'</button>' +
                    '<div class="m">Health '+metricCell(item.health)+' · Max CogCC '+item.cogcc+' · Smells '+item.smells+'</div>' +
                  '</div>').join('')+'</div>'
              : '<div class="offline-empty">No type hotspots match the current filter.</div>') +
          '</section>' +
          '<section class="offline-section"><h2>Method Hotspots</h2><div class="sub">Highest complexity methods in current filter.</div>' +
            (hotMethodRows.length
              ? '<div class="offline-list">'+hotMethodRows.map(item=>
                  '<div class="offline-item">' +
                    '<button class="offline-link t" data-type-id="'+escapeHtml(item.typeId)+'">'+escapeHtml(item.typeName)+' :: '+escapeHtml(item.methodName)+'</button>' +
                    '<div class="m">CogCC '+item.cogcc+' · CycCC '+item.cyccc+' · Nest '+item.nest+' · '+item.loc+' lines</div>' +
                  '</div>').join('')+'</div>'
              : '<div class="offline-empty">No method hotspots match the current filter.</div>') +
          '</section>' +
        '</div>' +
        '<section class="offline-section" id="offline-types"><h2>Types</h2><div class="sub">Click a type to open the detail panel.</div>' +
          '<div class="offline-table-wrap"><table class="offline-table"><thead><tr><th>Type</th><th>Assembly</th><th>Kind</th><th>Health</th><th>Max CogCC</th><th>CBO</th><th>DIT</th><th>Smells</th><th>Out</th><th>In</th></tr></thead><tbody>' +
            (filteredTypes.length
              ? filteredTypes.map(row=>
                  '<tr>' +
                    '<td><button class="offline-link" data-type-id="'+escapeHtml(row.id)+'">'+escapeHtml(row.qualifiedName)+'</button></td>' +
                    '<td>'+escapeHtml(row.assembly)+'</td>' +
                    '<td>'+escapeHtml(row.kind)+'</td>' +
                    '<td>'+metricCell(row.health,value=>'<span style="color:'+healthColor(value)+'">'+escapeHtml(value)+'</span>')+'</td>' +
                    '<td>'+row.maxCogCC+'</td>' +
                    '<td>'+metricCell(row.cbo)+'</td>' +
                    '<td>'+metricCell(row.dit)+'</td>' +
                    '<td>'+row.smellCount+'</td>' +
                    '<td>'+row.outgoing+'</td>' +
                    '<td>'+row.incoming+'</td>' +
                  '</tr>').join('')
              : '<tr><td colspan="10"><span class="offline-empty">No types match the current filter.</span></td></tr>') +
          '</tbody></table></div>' +
        '</section>' +
        '<section class="offline-section" id="offline-dependencies"><h2>Dependencies</h2>'+dependencyNote +
          '<div class="offline-table-wrap"><table class="offline-table"><thead><tr><th>From</th><th>Kind</th><th>To</th></tr></thead><tbody>' +
            (dependencyRows.length
              ? dependencyRows.map(item=>
                  '<tr>' +
                    '<td><button class="offline-link" data-type-id="'+escapeHtml(depFromId(item.dep))+'">'+escapeHtml(item.fromLabel)+'</button></td>' +
                    '<td><span class="offline-pill">'+escapeHtml(item.dep.kind)+'</span></td>' +
                    '<td><button class="offline-link" data-type-id="'+escapeHtml(depToId(item.dep))+'">'+escapeHtml(item.toLabel)+'</button></td>' +
                  '</tr>').join('')
              : '<tr><td colspan="3"><span class="offline-empty">No dependencies match the current filter.</span></td></tr>') +
          '</tbody></table></div>' +
        '</section>' +
        '<div class="offline-split">' +
          '<section class="offline-section" id="offline-cycles"><h2>Cycles</h2><div class="sub">Strongly connected components across type dependencies.</div>' +
            (cycles.length
              ? '<div class="offline-list">'+cycles.map((cycle,index)=>
                  '<div class="offline-item">' +
                    '<div class="t">Cycle '+(index+1)+' · '+cycle.length+' types</div>' +
                    '<div class="offline-pills">'+cycle.map(typeId=>
                      '<button class="offline-link offline-pill" data-type-id="'+escapeHtml(typeId)+'">'+escapeHtml(tl[typeId]?.name||typeId)+'</button>').join('')+'</div>' +
                  '</div>').join('')+'</div>'
              : '<div class="offline-empty">No circular dependencies detected.</div>') +
          '</section>' +
          '<section class="offline-section" id="offline-assemblies"><h2>Assembly Coupling</h2><div class="sub">Cross-assembly dependency counts and instability.</div>' +
            '<div class="offline-table-wrap"><table class="offline-table"><thead><tr><th>Assembly</th><th>Ca</th><th>Ce</th><th>Instability</th></tr></thead><tbody>' +
              assemblyData.coupling.map(item=>
                '<tr>' +
                  '<td>'+escapeHtml(item.name)+'</td>' +
                  '<td>'+item.ca+'</td>' +
                  '<td>'+item.ce+'</td>' +
                  '<td'+(item.instability>0.7?' style="color:#f97583"':'')+'>'+item.instability.toFixed(2)+'</td>' +
                '</tr>').join('') +
            '</tbody></table></div>' +
            '<div class="offline-table-wrap" style="margin-top:12px"><table class="offline-table"><thead><tr><th></th>' +
              assemblyData.names.map(name=>'<th>'+escapeHtml(name.split('.').pop())+'</th>').join('') +
            '</tr></thead><tbody>' +
              assemblyData.names.map(fromName=>
                '<tr><th>'+escapeHtml(fromName.split('.').pop())+'</th>' +
                  assemblyData.names.map(toName=>{
                    if(fromName===toName) return '<td><span class="offline-empty">-</span></td>';
                    const count=assemblyData.matrix.get(fromName).get(toName);
                    return '<td>'+(!count?'<span class="offline-empty">0</span>':count)+'</td>';
                  }).join('') +
                '</tr>').join('') +
            '</tbody></table></div>' +
          '</section>' +
        '</div>' +
      '</div>';
  }

  graphEl.addEventListener('click',event=>{
    const target=event.target.closest('[data-type-id]');
    if(!target) return;
    renderTypeDetail(target.dataset.typeId);
  });

  closeBtn.onclick=()=>panelEl.classList.add('hidden');

  searchEl.placeholder='Search types, namespaces, assemblies, dependencies...';
  searchEl.addEventListener('input',()=>render(searchEl.value));

  document.getElementById('bHot').onclick=()=>document.getElementById('offline-hotspots')?.scrollIntoView({behavior:'smooth',block:'start'});
  document.getElementById('bCyc').onclick=()=>document.getElementById('offline-cycles')?.scrollIntoView({behavior:'smooth',block:'start'});
  document.getElementById('bAsm').onclick=()=>document.getElementById('offline-assemblies')?.scrollIntoView({behavior:'smooth',block:'start'});

  let statsText=DATA.types.length+' types · '+DATA.dependencies.length+' deps · '+asm.length+' assemblies';
  if(avgHealth!=null)
    statsText+=' · Health: '+avgHealth;
  if(cycles.length)
    statsText+=' · '+cycles.length+' cycle'+(cycles.length===1?'':'s');
  statsEl.textContent=statsText;

  renderLegend();
  render(searchEl.value);
}

// --- Namespace info (direct types per namespace) ---
const nsInfo = new Map();
DATA.types.forEach(t=>{
  const ns = t.namespace||'(global)';
  if(!nsInfo.has(ns)) nsInfo.set(ns,{typeIds:[],asmCounts:{}});
  const info = nsInfo.get(ns);
  info.typeIds.push('t:'+typeKey(t));
  info.asmCounts[t.assembly]=(info.asmCounts[t.assembly]||0)+1;
});
nsInfo.forEach((info,ns)=>{
  const dom=Object.entries(info.asmCounts).sort((a,b)=>b[1]-a[1])[0][0];
  info.color=ac[dom]||'#6e7681';
  info.assembly=dom;
  info.count=info.typeIds.length;
});

// --- Build namespace tree ---
const nsTree = new Map();
const allPaths = new Set();
nsInfo.forEach((_,ns)=>{
  const parts = ns.split('.');
  for(let i=1;i<=parts.length;i++) allPaths.add(parts.slice(0,i).join('.'));
});

allPaths.forEach(path=>{
  const parts = path.split('.');
  const parentPath = parts.length>1 ? parts.slice(0,-1).join('.') : null;
  const info = nsInfo.get(path);
  nsTree.set(path,{
    shortName: parts[parts.length-1],
    parent: parentPath && allPaths.has(parentPath) ? parentPath : null,
    children: new Set(),
    directTypeCount: info ? info.count : 0,
    descendantTypeCount: 0,
    color: info ? info.color : null,
    virtual: !info,
    assembly: info ? info.assembly : null
  });
});

nsTree.forEach((node,path)=>{
  if(node.parent) nsTree.get(node.parent).children.add(path);
});

function computeDesc(path){
  const n=nsTree.get(path); let c=n.directTypeCount;
  n.children.forEach(ch=>{c+=computeDesc(ch)});
  return n.descendantTypeCount=c;
}

function inheritColor(path){
  const n=nsTree.get(path);
  n.children.forEach(ch=>inheritColor(ch));
  if(!n.color){
    let mc=0,mx='#6e7681';
    n.children.forEach(ch=>{
      const c=nsTree.get(ch);
      if(c.descendantTypeCount>mc){mc=c.descendantTypeCount;mx=c.color||mx}
    });
    n.color=mx;
  }
}

const roots=[];
nsTree.forEach((n,p)=>{if(!n.parent)roots.push(p)});
roots.forEach(r=>{computeDesc(r);inheritColor(r)});

const commonRoot=roots.length===1?roots[0]:null;

// --- Namespace health aggregates ---
const nsHealthMap = new Map();
function collectDescendantTypes(path){
  const names=[];
  const info=nsInfo.get(path);
  if(info) info.typeIds.forEach(id=>names.push(id.substring(2)));
  const n=nsTree.get(path);
  if(n) n.children.forEach(ch=>names.push(...collectDescendantTypes(ch)));
  return names;
}
nsTree.forEach((_,path)=>{
  const typeNames=collectDescendantTypes(path);
  const scores=typeNames.map(n=>tm[n]?.codeHealth).filter(s=>s!=null);
  if(!scores.length){nsHealthMap.set(path,null);return}
  const sorted=[...scores].sort((a,b)=>a-b);
  const worst=typeNames
    .map(n=>({name:n,h:tm[n]?.codeHealth}))
    .filter(x=>x.h!=null)
    .sort((a,b)=>a.h-b.h)
    .slice(0,5);
  nsHealthMap.set(path,{
    min:sorted[0], max:sorted[sorted.length-1],
    avg:+(scores.reduce((a,b)=>a+b,0)/scores.length).toFixed(1),
    total:scores.length,
    low:scores.filter(s=>s<7).length,
    crit:scores.filter(s=>s<4).length,
    worst:worst
  });
});

// --- Cytoscape elements ---
const els=[];

// For each tree node: summary + compound pair
nsTree.forEach((node,path)=>{
  els.push({group:'nodes',data:{
    id:'ns:'+path, label:node.shortName+'  ('+node.descendantTypeCount+')',
    fullLabel:path, shortName:node.shortName,
    nodeType:'namespace', tc:node.descendantTypeCount, color:node.color,
    assembly:node.assembly||'', virtual:node.virtual
  }});
  els.push({group:'nodes',data:{
    id:'cp:'+path, label:node.shortName, fullLabel:path, shortName:node.shortName,
    nodeType:'compound', color:node.color, assembly:node.assembly||'',
    virtual:node.virtual
  }});
});

// Type nodes with ownerNs + health color
DATA.types.forEach(t=>{
  const tk=typeKey(t);
  const m=tm[tk];
  const hc=m?healthColor(m.codeHealth):null;
  els.push({group:'nodes',data:{
    id:'t:'+tk, typeId:tk, label:t.name, nodeType:'type', kind:t.kind,
    assembly:t.assembly, color:ac[t.assembly]||'#6e7681',
    healthColor:hc, health:m?m.codeHealth:null,
    shape:KS[t.kind]||'round-rectangle', ownerNs:t.namespace||'(global)',
    mc:(t.members||[]).length
  }});
});

// Edge opacity per kind (structural edges prominent, usage edges subtle)
const EO={
  Inheritance:.85,InterfaceImpl:.75,FieldType:.35,PropertyType:.35,
  MethodParam:.28,ReturnType:.4,ConstructorParam:.45,EventType:.35,GenericConstraint:.28
};

// Type-level edges (namespace meta-edges built dynamically in rebuild)
const _pairCount=new Map();
DATA.dependencies.forEach((d,i)=>{
  const fromId=depFromId(d), toId=depToId(d);
  if(!tl[fromId]||!tl[toId]) return;
  const pk=[fromId,toId].sort().join('|');
  const idx=_pairCount.get(pk)||0;
  _pairCount.set(pk,idx+1);
});
const _pairIdx=new Map();
DATA.dependencies.forEach((d,i)=>{
  const fromId=depFromId(d), toId=depToId(d);
  if(!tl[fromId]||!tl[toId]) return;
  const st=DS[d.kind]||{s:'solid',w:1,a:'vee'};
  const pk=[fromId,toId].sort().join('|');
  const total=_pairCount.get(pk)||1;
  const ci=_pairIdx.get(pk)||0;
  _pairIdx.set(pk,ci+1);
  const step=25;
  const cpd=total<=1?0:(-((total-1)*step)/2+ci*step);
  els.push({group:'edges',data:{
    id:'e'+i, source:'t:'+fromId, target:'t:'+toId,
    kind:d.kind, color:DC[d.kind]||'#6e7681', ls:st.s, w:st.w, ar:st.a,
    cpd:[cpd], opa:EO[d.kind]||.45
  }});
});

// --- Init Cytoscape (deferred until fonts are ready when supported) ---
const fontReady=document.fonts&&document.fonts.ready?document.fonts.ready:Promise.resolve();
fontReady.then(()=>{
if(typeof cytoscape==='undefined'||typeof cytoscapeDagre==='undefined'){
  renderOfflineReport();
  return;
}
// Pre-measure label widths with loaded font to avoid Cytoscape clipping
const _mc=document.createElement('canvas').getContext('2d');
const _mf={type:'normal 10px "Cascadia Code","Fira Code","Consolas",monospace',ns:'600 12px "Cascadia Code","Fira Code","Consolas",monospace'};
function _mw(label,font){_mc.font=font;return Math.ceil(_mc.measureText(label).width)+8;}
els.forEach(e=>{
  if(e.group!=='nodes') return;
  const d=e.data;
  if(d.nodeType==='type') d.nw=_mw(d.label,_mf.type);
  else if(d.nodeType==='namespace') d.nw=_mw(d.label,_mf.ns);
});
const cy = cytoscape({
  container:document.getElementById('cy'),
  elements:els,
  style:[
    {selector:'node[nodeType="namespace"]',style:{
      'background-color':'data(color)','background-opacity':.15,
      'border-color':'data(color)','border-width':1.5,'border-style':'solid','border-opacity':.6,
      'label':'data(label)','font-family':'"Recursive",monospace','font-size':12,
      'font-weight':600,'color':'data(color)','text-valign':'center','text-halign':'center',
      'width':'data(nw)','height':'label','padding':20,'shape':'round-rectangle','text-max-width':9999
    }},
    {selector:'node[nodeType="namespace"][?virtual]',style:{'border-style':'dashed'}},
    {selector:'node[nodeType="compound"]',style:{
      'background-color':'data(color)','background-opacity':.06,
      'border-color':'data(color)','border-width':1.5,'border-opacity':.5,
      'border-style':'solid','shape':'round-rectangle',
      'label':'data(label)','font-family':'"Recursive",monospace','font-size':11,
      'font-weight':600,'color':'data(color)','text-opacity':.6,
      'text-valign':'top','text-halign':'center','text-margin-y':8,
      'padding':24,'min-width':60,'min-height':30,'text-max-width':9999,'corner-radius':6
    }},
    {selector:'node[nodeType="compound"][?virtual]',style:{'border-style':'dashed'}},
    {selector:'node[nodeType="type"]',style:{
      'background-color':'#0b0f19',
      'border-color':function(e){return e.data('healthColor')||e.data('color')},
      'border-width':1.5,
      'label':'data(label)','font-family':'"Recursive",monospace','font-size':10,
      'color':'#c9d1d9','text-valign':'center','text-halign':'center',
      'width':'data(nw)','height':'label','padding':18,'shape':'data(shape)','text-max-width':9999
    }},
    {selector:'node[nodeType="type"][kind="interface"]',style:{'border-style':'dashed','border-width':2}},
    {selector:'node[nodeType="type"][kind="enum"]',style:{'background-color':'#0f1420'}},
    {selector:'edge:not([?meta])',style:{
      'line-color':'data(color)','target-arrow-color':'data(color)',
      'target-arrow-shape':'data(ar)','arrow-scale':.6,
      'width':'data(w)','line-style':'data(ls)',
      'curve-style':'taxi',
      'taxi-direction':'downward','taxi-turn':20,'taxi-turn-min-distance':8,
      'opacity':'data(opa)'
    }},
    {selector:'edge[?meta]',style:{
      'line-color':'#8b949e','target-arrow-color':'#8b949e',
      'target-arrow-shape':'triangle','width':1.5,'line-style':'dashed',
      'curve-style':'taxi','taxi-direction':'downward','taxi-turn':30,'taxi-turn-min-distance':10,
      'opacity':.5,
      'label':'data(label)','font-size':9,'color':'#c9d1d9',
      'text-background-color':'#131825','text-background-opacity':1,
      'text-background-padding':4,'text-background-shape':'round-rectangle',
      'text-margin-y':-12
    }},
    {selector:'node:selected',style:{'border-width':2.5,'border-color':'#58a6ff'}},
    {selector:'.dim',style:{'opacity':.12}},
    {selector:'.hl',style:{'opacity':1,'border-width':3,'z-index':999}},
    {selector:'.cycle',style:{'border-color':'#f97583','border-width':3,'opacity':1,'z-index':999}},
    {selector:'.cycle-edge',style:{'line-color':'#f97583','target-arrow-color':'#f97583','opacity':1,'width':2.5,'z-index':999}}
  ],
  layout:{name:'preset'},
  wheelSensitivity:.3,minZoom:.08,maxZoom:5
});

// --- Middle-button pan ---
{
  const container=cy.container();
  let _mbPan=false, _mbX=0, _mbY=0;
  container.addEventListener('mousedown',e=>{
    if(e.button===1){e.preventDefault();_mbPan=true;_mbX=e.clientX;_mbY=e.clientY;container.style.cursor='grabbing'}
  });
  window.addEventListener('mousemove',e=>{
    if(!_mbPan) return;
    cy.panBy({x:e.clientX-_mbX,y:e.clientY-_mbY});
    _mbX=e.clientX;_mbY=e.clientY;
  });
  window.addEventListener('mouseup',e=>{
    if(e.button===1&&_mbPan){_mbPan=false;container.style.cursor=''}
  });
  container.addEventListener('auxclick',e=>{if(e.button===1) e.preventDefault()});
}

// --- State ---
const expanded = new Set();
if(commonRoot) expanded.add(commonRoot);

// --- Visibility helpers ---
function isVisible(ns){
  const node=nsTree.get(ns); if(!node) return false;
  let cur=node.parent;
  while(cur && nsTree.has(cur)){
    if(!expanded.has(cur)) return false;
    cur=nsTree.get(cur).parent;
  }
  return true;
}

function findVisibleAncestor(ns){
  let cur=ns;
  while(cur){
    if(isVisible(cur)&&!expanded.has(cur)) return 'ns:'+cur;
    const node=nsTree.get(cur);
    cur=node?node.parent:null;
  }
  return null;
}

function collapseDescendants(ns){
  const node=nsTree.get(ns); if(!node) return;
  node.children.forEach(ch=>{expanded.delete(ch);collapseDescendants(ch)});
}

// --- Rebuild view ---
function rebuild(){
  cy.startBatch();
  cy.elements('edge[?meta]').remove();

  nsTree.forEach((node,path)=>{
    const nsN=cy.getElementById('ns:'+path);
    const cpN=cy.getElementById('cp:'+path);
    const vis=isVisible(path);
    const exp=expanded.has(path);

    if(vis&&!exp){
      // Summary mode: visible but collapsed
      nsN.style('display','element');
      cpN.style('display','none');
      if(node.parent&&isVisible(node.parent)&&expanded.has(node.parent)){
        if(!nsN.parent().length||nsN.parent().id()!=='cp:'+node.parent) nsN.move({parent:'cp:'+node.parent});
      } else {
        if(nsN.parent().length) nsN.move({parent:null});
      }
      if(cpN.parent().length) cpN.move({parent:null});
    } else if(vis&&exp){
      // Compound mode: visible and expanded
      nsN.style('display','none');
      cpN.style('display','element');
      if(node.parent&&isVisible(node.parent)&&expanded.has(node.parent)){
        if(!cpN.parent().length||cpN.parent().id()!=='cp:'+node.parent) cpN.move({parent:'cp:'+node.parent});
      } else {
        if(cpN.parent().length) cpN.move({parent:null});
      }
      if(nsN.parent().length) nsN.move({parent:null});
    } else {
      // Hidden
      nsN.style('display','none');
      cpN.style('display','none');
      if(nsN.parent().length) nsN.move({parent:null});
      if(cpN.parent().length) cpN.move({parent:null});
    }
  });

  // Type nodes
  cy.nodes('[nodeType="type"]').forEach(n=>{
    const ns=n.data('ownerNs');
    if(isVisible(ns)&&expanded.has(ns)){
      const cpId='cp:'+ns;
      if(!n.parent().length||n.parent().id()!==cpId) n.move({parent:cpId});
      n.style('display','element');
    } else {
      if(n.parent().length) n.move({parent:null});
      n.style('display','none');
    }
  });

  // Meta-edges: route hidden endpoints to nearest visible ancestor
  const mm=new Map();
  cy.edges().filter(e=>!e.data('meta')).forEach(e=>{
    const sv=e.source().style('display')!=='none';
    const tv=e.target().style('display')!=='none';
    if(sv&&tv){e.style('display','element');return}
    e.style('display','none');

    const si=sv?e.source().id():findVisibleAncestor(e.source().data('ownerNs'));
    const ti=tv?e.target().id():findVisibleAncestor(e.target().data('ownerNs'));
    if(!si||!ti||si===ti) return;
    const sn=cy.getElementById(si),tn=cy.getElementById(ti);
    if(sn.empty()||tn.empty()||sn.style('display')==='none'||tn.style('display')==='none') return;

    const k=si+'>'+ti;
    mm.set(k,(mm.get(k)||0)+1);
  });

  let mi=0;
  mm.forEach((cnt,k)=>{
    const p=k.split('>');
    cy.add({group:'edges',data:{id:'m'+(mi++),source:p[0],target:p[1],label:''+cnt,meta:true}});
  });

  cy.endBatch();
}

let _layoutEngine='elk';

function layout(){
  const vis=cy.elements().filter(e=>e.style('display')!=='none');
  if(_layoutEngine==='elk'&&typeof ELK!=='undefined'){
    layoutElk(vis);
  } else {
    layoutDagre(vis);
  }
}

function layoutDagre(vis){
  vis.layout({
    name:'dagre',rankDir:'TB',nodeSep:80,rankSep:100,edgeSep:30,
    animate:true,animationDuration:250,fit:true,padding:40
  }).run();
}

function layoutElk(vis){
  vis.layout({
    name:'elk',
    elk:{
      algorithm:'layered',
      'elk.direction':'DOWN',
      'elk.edgeRouting':'ORTHOGONAL',
      'elk.spacing.nodeNode':'80',
      'elk.layered.spacing.nodeNodeBetweenLayers':'100',
      'elk.layered.spacing.edgeEdgeBetweenLayers':'30',
      'elk.layered.spacing.edgeNodeBetweenLayers':'30',
      'elk.layered.crossingMinimization.strategy':'LAYER_SWEEP',
      'elk.hierarchyHandling':'INCLUDE_CHILDREN',
    },
    animate:true,animationDuration:250,fit:true,padding:40
  }).run();
}

function colAll(){
  expanded.clear();
  if(commonRoot) expanded.add(commonRoot);
  rebuild();layout();
}
function expAll(){
  nsTree.forEach((_,p)=>expanded.add(p));
  rebuild();layout();
}

// --- Interactions ---
cy.on('dblclick','node[nodeType="namespace"]',e=>{
  expanded.add(e.target.data('fullLabel'));
  rebuild();layout();
});
cy.on('dblclick','node[nodeType="compound"]',e=>{
  const ns=e.target.data('fullLabel');
  expanded.delete(ns);
  collapseDescendants(ns);
  rebuild();layout();
});
cy.on('dblclick','node[nodeType="type"]',e=>{
  const ns=e.target.data('ownerNs');
  expanded.delete(ns);
  collapseDescendants(ns);
  rebuild();layout();
});

// Initial render
rebuild();layout();

// --- Health Badges ---
const badgeEl=document.getElementById('badges');
const btipEl=document.getElementById('btip');
let _badgeRaf=0;

// LOD thresholds — inspired by map apps
const BADGE_ZOOM_REF=1;
const BADGE_SCALE_MIN=0.45;
const BADGE_SCALE_MAX=1.6;
const BADGE_MIN_PX=10;
const LOD_THRESHOLDS=[
  {zoom:0.15, maxScore:4},
  {zoom:0.30, maxScore:7},
  {zoom:0.50, maxScore:9},
];

function badgeLodVisible(score, zoom){
  for(const t of LOD_THRESHOLDS){
    if(zoom<t.zoom) return score<t.maxScore;
  }
  return true;
}

// Snapshot state: badges are positioned at these viewport params
let _snapPan={x:0,y:0}, _snapZoom=0;
let _badgesDirty=true; // forces full rebuild

function markBadgesDirty(){ _badgesDirty=true; }

function rebuildBadges(){
  cancelAnimationFrame(_badgeRaf);
  _badgeRaf=requestAnimationFrame(()=>{
    _badgesDirty=false;
    badgeEl.style.transform='';
    badgeEl.innerHTML='';
    const zoom=cy.zoom();
    const pan=cy.pan();
    _snapPan={x:pan.x,y:pan.y};
    _snapZoom=zoom;
    const rawScale=Math.sqrt(zoom/BADGE_ZOOM_REF);
    const scale=Math.max(BADGE_SCALE_MIN,Math.min(BADGE_SCALE_MAX,rawScale));
    const vw=badgeEl.clientWidth, vh=badgeEl.clientHeight;
    const margin=30;

    // Phase 1: collect badge data
    const raw=[];
    cy.nodes().forEach(n=>{
      if(n.style('display')==='none') return;
      const d=n.data();
      let score=null,isNs=false,nsPath=null;
      if(d.nodeType==='type'){
        const m=tm[d.typeId];
        if(!m) return;
        score=m.codeHealth;
      } else if(d.nodeType==='namespace'||d.nodeType==='compound'){
        const h=nsHealthMap.get(d.fullLabel);
        if(!h) return;
        score=h.min; isNs=true; nsPath=d.fullLabel;
      } else return;

      const bb=n.renderedBoundingBox();
      if(bb.x2<-margin||bb.x1>vw+margin||bb.y2<-margin||bb.y1>vh+margin) return;

      const renderedSize=(isNs?22:18)*scale;
      if(renderedSize<BADGE_MIN_PX&&zoom>=0.5) return;

      raw.push({x:bb.x2,y:bb.y1,score,isNs,nsPath:nsPath||'',
        nodeId:n.id(),typeId:d.typeId||'',count:1,scores:[score]});
    });

    // Phase 2: grid-based clustering when zoomed out
    let items;
    if(zoom<0.5&&raw.length>3){
      const cellSize=Math.max(40, 60/Math.max(zoom,0.05));
      const grid=new Map();
      raw.forEach(b=>{
        const key=Math.floor(b.x/cellSize)+','+Math.floor(b.y/cellSize);
        if(!grid.has(key)) grid.set(key,[]);
        grid.get(key).push(b);
      });
      items=[];
      grid.forEach(cell=>{
        if(cell.length===1){items.push(cell[0]);return}
        let minS=Infinity,sx=0,sy=0,anyNs=false;
        const allScores=[];
        cell.forEach(b=>{
          if(b.score<minS) minS=b.score;
          sx+=b.x; sy+=b.y;
          if(b.isNs) anyNs=true;
          allScores.push(b.score);
        });
        items.push({x:sx/cell.length,y:sy/cell.length,score:minS,
          isNs:anyNs,nsPath:'',nodeId:cell[0].nodeId,typeId:'',
          count:cell.length,scores:allScores.sort((a,c)=>a-c)});
      });
    } else {
      items=raw;
    }

    // Phase 3: render
    items.forEach(bd=>{
      const isCluster=bd.count>1;
      if(!isCluster&&!badgeLodVisible(bd.score, zoom)) return;

      const b=document.createElement('div');
      b.className=isCluster?'hb hb-cluster':(bd.isNs?'hb hb-ns':'hb');
      b.style.left=bd.x+'px';
      b.style.top=bd.y+'px';
      b.style.background=healthColor(bd.score);
      b.style.transform='translate(50%,-50%) scale('+scale.toFixed(3)+')';
      let opacity=1;
      for(const t of LOD_THRESHOLDS){
        if(zoom<t.zoom && bd.score<t.maxScore){
          const fadeRange=t.zoom*0.3;
          const fadeStart=t.zoom-fadeRange;
          if(zoom<fadeStart) opacity=Math.max(0.4, (zoom-fadeStart*0.5)/(fadeStart*0.5));
          break;
        }
      }
      if(opacity<1) b.style.opacity=opacity.toFixed(2);

      if(isCluster){
        b.textContent=bd.score>=10?'10':bd.score.toFixed(1);
        const cnt=document.createElement('span');
        cnt.className='hbc';
        cnt.textContent='\u00d7'+bd.count;
        b.appendChild(cnt);
      } else {
        b.textContent=bd.score>=10?'10':bd.score.toFixed(1);
      }

      b.dataset.nodeId=bd.nodeId;
      b.dataset.isNs=bd.isNs?'1':'';
      b.dataset.nsPath=bd.nsPath;
      b.dataset.typeId=bd.typeId;
      b.dataset.cluster=isCluster?'1':'';
      b.dataset.scores=isCluster?bd.scores.join(','):'';
      b.dataset.count=bd.count;
      badgeEl.appendChild(b);
    });
  });
}

// Viewport handler: fast pan path vs full rebuild
cy.on('viewport',()=>{
  const zoom=cy.zoom();
  const pan=cy.pan();
  // Zoom changed or dirty → full rebuild (LOD + scale recalc)
  if(_badgesDirty || Math.abs(zoom-_snapZoom)>0.0001){
    rebuildBadges();
    return;
  }
  // Pan only → translate container (synchronous, zero lag, GPU composited)
  badgeEl.style.transform=`translate(${pan.x-_snapPan.x}px,${pan.y-_snapPan.y}px)`;
});
// Structural changes (expand/collapse, layout, search filter)
cy.on('layoutstop',()=>{ markBadgesDirty(); rebuildBadges(); });
// Node drag → sync update existing badge positions (no DOM rebuild, zero lag)
function updateBadgePositions(){
  cancelAnimationFrame(_badgeRaf);
  badgeEl.style.transform='';
  const zoom=cy.zoom(), pan=cy.pan();
  _snapPan={x:pan.x,y:pan.y}; _snapZoom=zoom;
  const children=badgeEl.children;
  for(let i=0;i<children.length;i++){
    const b=children[i], n=cy.getElementById(b.dataset.nodeId);
    if(!n||n.removed()||n.style('display')==='none') continue;
    const bb=n.renderedBoundingBox();
    b.style.left=bb.x2+'px'; b.style.top=bb.y1+'px';
  }
  _badgesDirty=false;
}
cy.on('drag','node', updateBadgePositions);
cy.on('free','node',()=>{ markBadgesDirty(); rebuildBadges(); });

badgeEl.addEventListener('mouseover',e=>{
  const b=e.target.closest('.hb');
  if(!b) return;
  let h='';
  if(b.dataset.cluster==='1'){
    const scores=(b.dataset.scores||'').split(',').map(Number);
    const cnt=+b.dataset.count||scores.length;
    const min=scores[0],max=scores[scores.length-1];
    const avg=+(scores.reduce((a,v)=>a+v,0)/scores.length).toFixed(1);
    const low=scores.filter(s=>s<7).length;
    const crit=scores.filter(s=>s<4).length;
    h='<div class="bh" style="color:'+healthColor(min)+'">Cluster ('+cnt+' badges)</div>';
    h+='<div class="br"><span class="bk">worst</span><span class="bv" style="color:'+healthColor(min)+'">'+min.toFixed(1)+'</span></div>';
    h+='<div class="br"><span class="bk">avg</span><span class="bv">'+avg+'</span></div>';
    h+='<div class="br"><span class="bk">best</span><span class="bv" style="color:'+healthColor(max)+'">'+max.toFixed(1)+'</span></div>';
    if(low>0) h+='<div class="br"><span class="bk">warn</span><span class="bw">'+low+' / '+cnt+' below 7</span></div>';
    if(crit>0) h+='<div class="br"><span class="bk">crit</span><span class="bw">'+crit+' below 4</span></div>';
  } else if(b.dataset.isNs==='1'){
    const d=nsHealthMap.get(b.dataset.nsPath);
    if(!d) return;
    h='<div class="bh" style="color:'+healthColor(d.min)+'">'+esc(b.dataset.nsPath)+'</div>';
    h+='<div class="br"><span class="bk">min</span><span class="bv" style="color:'+healthColor(d.min)+'">'+d.min.toFixed(1)+'</span></div>';
    h+='<div class="br"><span class="bk">avg</span><span class="bv">'+d.avg+'</span></div>';
    h+='<div class="br"><span class="bk">max</span><span class="bv">'+d.max.toFixed(1)+'</span></div>';
    if(d.low>0) h+='<div class="br"><span class="bk">warn</span><span class="bw">'+d.low+' / '+d.total+' types below 7</span></div>';
    if(d.crit>0) h+='<div class="br"><span class="bk">crit</span><span class="bw">'+d.crit+' types below 4</span></div>';
    if(d.worst.length){
      h+='<div style="margin-top:4px;padding-top:4px;border-top:1px solid var(--border);font-size:9px;color:var(--dim)">Lowest</div>';
      d.worst.forEach(w=>{
        h+='<div class="br"><span class="bv" style="color:'+healthColor(w.h)+'">'+w.h.toFixed(1)+'</span><span class="bv">'+esc(w.name)+'</span></div>';
      });
    }
  } else {
    const m=tm[b.dataset.typeId];
    if(!m) return;
    h='<div class="bh" style="color:'+healthColor(m.codeHealth)+'">'+esc(m.typeName)+'</div>';
    h+='<div class="br"><span class="bk">health</span><span class="bv" style="color:'+healthColor(m.codeHealth)+'">'+m.codeHealth+'</span></div>';
    h+='<div class="br"><span class="bk">CogCC</span><span class="bv">avg '+m.averageCognitiveComplexity+' / max '+m.maxCognitiveComplexity+'</span></div>';
    h+='<div class="br"><span class="bk">CycCC</span><span class="bv">avg '+m.averageCyclomaticComplexity+' / max '+m.maxCyclomaticComplexity+'</span></div>';
    h+='<div class="br"><span class="bk">nest</span><span class="bv">max '+m.maxNestingDepth+'</span></div>';
    h+='<div class="br"><span class="bk">LOC</span><span class="bv">'+m.lineCount+'</span></div>';
    h+='<div class="br"><span class="bk">methods</span><span class="bv">'+m.methodCount+'</span></div>';
    if(m.lcom!=null)
      h+='<div class="br"><span class="bk">LCOM</span><span class="bv'+(m.lcom>=0.8?' bw':'')+'">'+(+m.lcom).toFixed(2)+'</span></div>';
    if(m.cbo!=null)
      h+='<div class="br"><span class="bk">CBO</span><span class="bv'+(m.cbo>=14?' bw':'')+'">'+m.cbo+'</span></div>';
    if(m.dit!=null&&m.dit>0)
      h+='<div class="br"><span class="bk">DIT</span><span class="bv'+(m.dit>=6?' bw':'')+'">'+m.dit+'</span></div>';
    if(m.minMaintainabilityIndex!=null)
      h+='<div class="br"><span class="bk">MI</span><span class="bv'+(m.minMaintainabilityIndex<20?' bw':'')+'">'+m.minMaintainabilityIndex+'</span></div>';
    if(m.instability!=null)
      h+='<div class="br"><span class="bk">I</span><span class="bv">'+(+m.instability).toFixed(2)+'</span></div>';
    if(m.excessiveParameterMethodCount>0)
      h+='<div class="br"><span class="bk">!</span><span class="bw">'+m.excessiveParameterMethodCount+' methods >4 params</span></div>';
    if(m.codeSmells&&m.codeSmells.length)
      h+='<div class="br"><span class="bk">smells</span><span class="bw">'+m.codeSmells.length+'</span></div>';
  }
  btipEl.innerHTML=h;
  const r=b.getBoundingClientRect();
  const bw=document.body.clientWidth,bh2=document.body.clientHeight;
  const tw=btipEl.offsetWidth||220,th=btipEl.offsetHeight||100;
  let tx=r.right+6,ty=r.top;
  if(tx+tw>bw-10) tx=r.left-tw-6;
  if(ty+th>bh2-10) ty=bh2-th-10;
  btipEl.style.left=tx+'px';btipEl.style.top=ty+'px';
  btipEl.classList.add('show');
});
badgeEl.addEventListener('mouseout',e=>{
  if(e.target.closest('.hb')) btipEl.classList.remove('show');
});

// --- Detail Panel ---
const dp=document.getElementById('dp'),dc=document.getElementById('dc');
function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')}

cy.on('tap','node[nodeType="type"]',e=>{
  const t=tl[e.target.data('typeId')];
  if(!t)return;
  const c=ac[t.assembly]||'#6e7681';
  const tk=typeKey(t);
  const mx=tm[tk];
  let h='<h2 style="color:'+c+'">'+esc(t.name)+'</h2>';
  h+='<div class="meta">'+esc(t.kind)+' &middot; '+esc(t.assembly)+'</div>';
  if(t.namespace)h+='<div class="meta">'+esc(t.namespace)+'</div>';

  if(mx){
    const hcol=healthColor(mx.codeHealth);
    h+='<div class="section-title">Code Health</div>';
    h+='<div class="member"><span class="badge" style="background:'+hcol+';color:#0b0f19;font-weight:600">'+mx.codeHealth+'</span>';
    h+='<span class="n">Score</span></div>';
    h+='<div class="member"><span class="badge">CogCC</span><span class="n">avg '+mx.averageCognitiveComplexity+' / max '+mx.maxCognitiveComplexity+'</span></div>';
    h+='<div class="member"><span class="badge">CycCC</span><span class="n">avg '+mx.averageCyclomaticComplexity+' / max '+mx.maxCyclomaticComplexity+'</span></div>';
    h+='<div class="member"><span class="badge">Nest</span><span class="n">max '+mx.maxNestingDepth+'</span></div>';
    h+='<div class="member"><span class="badge">LOC</span><span class="n">'+mx.lineCount+' lines</span></div>';
    h+='<div class="member"><span class="badge">M</span><span class="n">'+mx.methodCount+' methods</span></div>';
    if(mx.lcom!=null)
      h+='<div class="member"><span class="badge"'+(mx.lcom>=0.8?' style="color:#f97583"':'')+'>LCOM</span><span class="n">'+(+mx.lcom).toFixed(2)+'</span></div>';
    if(mx.cbo!=null)
      h+='<div class="member"><span class="badge"'+(mx.cbo>=14?' style="color:#f97583"':'')+'>CBO</span><span class="n">'+mx.cbo+' coupled types</span></div>';
    if(mx.dit!=null&&mx.dit>0)
      h+='<div class="member"><span class="badge"'+(mx.dit>=6?' style="color:#f97583"':'')+'>DIT</span><span class="n">depth '+mx.dit+'</span></div>';
    if(mx.afferentCoupling!=null||mx.efferentCoupling!=null){
      h+='<div class="member"><span class="badge">Ca/Ce</span><span class="n">'+(mx.afferentCoupling||0)+' / '+(mx.efferentCoupling||0)+'</span></div>';
    }
    if(mx.instability!=null)
      h+='<div class="member"><span class="badge">I</span><span class="n">'+(+mx.instability).toFixed(2)+(mx.instability>0.7?' (unstable)':mx.instability<0.3?' (stable)':'')+'</span></div>';
    if(mx.averageMaintainabilityIndex!=null)
      h+='<div class="member"><span class="badge"'+(mx.minMaintainabilityIndex!=null&&mx.minMaintainabilityIndex<20?' style="color:#f97583"':'')+'>MI</span><span class="n">avg '+(+mx.averageMaintainabilityIndex).toFixed(1)+(mx.minMaintainabilityIndex!=null?' / min '+(+mx.minMaintainabilityIndex).toFixed(1):'')+'</span></div>';
    if(mx.excessiveParameterMethodCount>0)
      h+='<div class="member"><span class="badge" style="color:#f97583">!</span><span class="n">'+mx.excessiveParameterMethodCount+' methods with >4 params</span></div>';
    if(mx.codeSmells&&mx.codeSmells.length){
      h+='<div class="section-title">Code Smells ('+mx.codeSmells.length+')</div>';
      mx.codeSmells.forEach(s=>{
        const sc=s.severity==='Critical'?'#ff7b72':'#e3b341';
        h+='<div class="member"><span class="badge" style="color:'+sc+'">'+s.severity[0]+'</span>';
        h+='<span class="n">'+esc(s.kind)+(s.methodName?' &middot; '+esc(s.methodName):'')+' &middot; '+esc(s.message)+'</span></div>';
      });
    }
  }

  if(t.baseType){h+='<div class="section-title">Base Type</div>';
    h+='<div class="member"><span class="t">'+esc(t.baseType)+'</span></div>';}
  if(t.interfaces&&t.interfaces.length){h+='<div class="section-title">Interfaces</div>';
    t.interfaces.forEach(i=>{h+='<div class="member"><span class="t">'+esc(i)+'</span></div>';});}
  if(t.attributes&&t.attributes.length){h+='<div class="section-title">Attributes</div>';
    t.attributes.forEach(a=>{
      let x='['+a.name;
      if(a.arguments){x+='('+Object.entries(a.arguments).map(([k,v])=>k+'='+v).join(', ')+')'}
      h+='<div class="member"><span class="n">'+esc(x+']')+'</span></div>';
    });}
  if(t.members&&t.members.length){
    h+='<div class="section-title">Members ('+t.members.length+')</div>';
    t.members.forEach(m=>{
      let ccBadge='';
      if(m.cognitiveComplexity!=null){
        const ccCol=m.cognitiveComplexity>15?'#f97583':m.cognitiveComplexity>8?'#e3b341':'#7ee787';
        ccBadge='<span class="badge" style="color:'+ccCol+'">CogCC:'+m.cognitiveComplexity+'</span>';
        if(m.cyclomaticComplexity!=null) ccBadge+='<span class="badge">CycCC:'+m.cyclomaticComplexity+'</span>';
        if(m.maxNestingDepth!=null&&m.maxNestingDepth>0) ccBadge+='<span class="badge">D:'+m.maxNestingDepth+'</span>';
        if(m.lineCount>0) ccBadge+='<span class="badge">'+m.lineCount+'L</span>';
      }
      h+='<div class="member"><span class="badge">'+m.memberKind[0]+'</span>';
      h+=ccBadge;
      h+='<span class="n">'+esc(m.name)+'</span> ';
      h+='<span class="t">'+esc(m.type)+'</span></div>';
    });}

  const og=DATA.dependencies.filter(d=>depFromId(d)===tk);
  const ic=DATA.dependencies.filter(d=>depToId(d)===tk);
  if(og.length){h+='<div class="section-title">Depends On ('+og.length+')</div>';
    og.forEach(d=>{
      const toType=tl[depToId(d)];
      h+='<div class="member"><span class="badge" style="color:'+(DC[d.kind]||'#6e7681')+'">'+d.kind+'</span>';
      h+='<span class="t">'+esc(toType?toType.name:d.toType)+'</span></div>';
    });}
  if(ic.length){h+='<div class="section-title">Depended By ('+ic.length+')</div>';
    ic.forEach(d=>{
      const fromType=tl[depFromId(d)];
      h+='<div class="member"><span class="badge" style="color:'+(DC[d.kind]||'#6e7681')+'">'+d.kind+'</span>';
      h+='<span class="t">'+esc(fromType?fromType.name:d.fromType)+'</span></div>';
    });}

  dc.innerHTML=h;dp.classList.remove('hidden');
});

cy.on('tap','node[nodeType="namespace"],node[nodeType="compound"]',e=>{
  const ns=e.target.data('fullLabel');
  const tn=nsTree.get(ns);
  if(!tn) return;
  const c=tn.color||'#6e7681';
  let h='<h2 style="color:'+c+'">'+esc(ns)+'</h2>';
  h+='<div class="meta">';
  if(tn.virtual) h+='virtual &middot; ';
  h+=tn.descendantTypeCount+' types ('+tn.directTypeCount+' direct)';
  if(tn.assembly) h+=' &middot; '+esc(tn.assembly);
  h+='</div>';

  if(tn.children.size){
    h+='<div class="section-title">Sub-namespaces ('+tn.children.size+')</div>';
    tn.children.forEach(ch=>{
      const cn=nsTree.get(ch);
      h+='<div class="member"><span class="badge">NS</span>';
      h+='<span class="n">'+esc(cn.shortName)+'</span> ';
      h+='<span class="t">('+cn.descendantTypeCount+')</span></div>';
    });
  }

  if(tn.directTypeCount){
    h+='<div class="section-title">Direct Types ('+tn.directTypeCount+')</div>';
    const types=DATA.types.filter(t=>(t.namespace||'(global)')===ns);
    types.forEach(t=>{
      h+='<div class="member"><span class="badge">'+t.kind[0].toUpperCase()+'</span>';
      h+='<span class="n">'+esc(t.name)+'</span></div>';
    });
  }

  dc.innerHTML=h;dp.classList.remove('hidden');
});

cy.on('tap',e=>{if(e.target===cy)dp.classList.add('hidden')});
document.getElementById('cls').onclick=()=>dp.classList.add('hidden');

// --- Search ---
let st;
document.getElementById('q').addEventListener('input',function(){
  clearTimeout(st);
  st=setTimeout(()=>{
    const v=this.value.trim().toLowerCase();
    if(!v){cy.elements().removeClass('dim hl');return}
    cy.elements().addClass('dim');
    const mTypes=cy.nodes('[nodeType="type"]').filter(n=>
      n.style('display')!=='none'&&n.data('label').toLowerCase().includes(v));
    const mNs=cy.nodes('[nodeType="namespace"]').filter(n=>
      n.style('display')!=='none'&&n.data('fullLabel').toLowerCase().includes(v));
    const mCp=cy.nodes('[nodeType="compound"]').filter(n=>
      n.style('display')!=='none'&&n.data('fullLabel').toLowerCase().includes(v));
    const matched=mTypes.merge(mNs).merge(mCp);
    matched.removeClass('dim').addClass('hl');
    matched.connectedEdges().filter(e=>e.style('display')!=='none').removeClass('dim');
    matched.connectedEdges().connectedNodes().filter(n=>n.style('display')!=='none').removeClass('dim');
    if(matched.length)cy.animate({fit:{eles:matched,padding:60},duration:250});
  },180);
});

// --- Edge Style Toggle (Bezier / Taxi / ELK) ---
document.getElementById('edgeStyle').addEventListener('change',function(){
  const mode=this.value;
  const prevEngine=_layoutEngine;
  _layoutEngine=(mode==='elk')?'elk':'dagre';
  cy.startBatch();
  if(mode==='taxi'||mode==='elk'){
    cy.edges(':not([?meta])').style({
      'curve-style':'taxi','taxi-direction':'downward',
      'taxi-turn':20,'taxi-turn-min-distance':8
    });
  } else {
    cy.edges(':not([?meta])').style({
      'curve-style':'unbundled-bezier',
      'control-point-distances':'data(cpd)',
      'control-point-weights':[0.5]
    }).removeStyle('taxi-direction taxi-turn taxi-turn-min-distance');
  }
  cy.endBatch();
  if(_layoutEngine!==prevEngine){rebuild();layout();}
});

// --- Edge Kind Filter ---
const _edgeVis=new Map();
Object.keys(DC).forEach(k=>_edgeVis.set(k,true));
const efPanel=document.getElementById('efPanel');
(function buildEdgeFilter(){
  let h='';
  Object.entries(DC).forEach(([k,c])=>{
    const d=DS[k]||{s:'solid'};
    h+='<label class="ef-row"><input type="checkbox" class="ef-cb" data-kind="'+k+'" checked>';
    h+='<span class="ef-swatch" style="border-bottom:2px '+d.s+' '+c+'"></span>'+k+'</label>';
  });
  efPanel.innerHTML=h;
  efPanel.addEventListener('change',e=>{
    const cb=e.target;
    if(!cb.dataset.kind) return;
    _edgeVis.set(cb.dataset.kind,cb.checked);
    applyEdgeFilter();
  });
})();
function applyEdgeFilter(){
  cy.startBatch();
  cy.edges(':not([?meta])').forEach(e=>{
    const vis=_edgeVis.get(e.data('kind'));
    e.style('display',vis?'element':'none');
  });
  cy.endBatch();
  markBadgesDirty();rebuildBadges();
}
document.getElementById('bEdge').onclick=function(){
  const btn=this;
  const showing=efPanel.classList.toggle('show');
  btn.classList.toggle('active',showing);
  if(showing){
    const r=btn.getBoundingClientRect();
    efPanel.style.left=r.left+'px';
  }
};

// --- Toolbar ---
document.getElementById('bExp').onclick=expAll;
document.getElementById('bCol').onclick=colAll;
document.getElementById('bFit').onclick=()=>{
  const vis=cy.elements().filter(e=>e.style('display')!=='none');
  cy.fit(vis,30);
};
document.getElementById('bLay').onclick=()=>{rebuild();layout()};

// --- Legend ---
(function(){
  let h='<b>Assemblies</b> ';
  asm.forEach(a=>{
    const c=ac[a.name]||'#6e7681';
    const s=a.name.split('.').pop();
    h+='<div class="li"><div class="sw" style="background:'+c+'"></div>'+s+'</div>';
  });
  h+='<div class="sep" style="width:1px;height:14px;background:#1e2538"></div><b>Edges</b> ';
  Object.entries(DC).forEach(([k,c])=>{
    const d=DS[k]||{s:'solid'};
    h+='<div class="li"><div class="el" style="border-bottom:2px '+d.s+' '+c+'"></div>'+k+'</div>';
  });
  document.getElementById('lg').innerHTML=h;
})();

let stText=DATA.types.length+' types \u00b7 '+DATA.dependencies.length+' deps \u00b7 '+asm.length+' assemblies';
const asmWithHealth=asm.filter(a=>a.healthMetrics);
if(asmWithHealth.length){
  const avgH=(asmWithHealth.reduce((s,a)=>s+a.healthMetrics.averageCodeHealth,0)/asmWithHealth.length).toFixed(1);
  stText+=' \u00b7 Health: '+avgH;
}
document.getElementById('st').textContent=stText;

// --- Tooltip ---
const tip=document.getElementById('tip');
let tipNode=null;

function showTip(node,px,py){
  tipNode=node;
  const d=node.data();
  let h='';
  if(d.nodeType==='type'){
    const t=tl[d.typeId];
    if(!t){tip.classList.remove('show');return}
    const mods=(t.modifiers||[]).join(' ');
    const tk=typeKey(t);
    const tmx=tm[tk];
    h+='<h3 style="color:'+(ac[t.assembly]||'#6e7681')+'">'+esc(t.name)+'</h3>';
    h+='<div class="tm">'+esc(mods+' '+t.kind)+' in '+esc(t.namespace||'(global)');
    if(tmx) h+=' &middot; Health: <span style="color:'+healthColor(tmx.codeHealth)+'">'+tmx.codeHealth+'</span>';
    h+='</div>';
    if(tmx){
      h+='<div class="tl"><span class="tk">CogCC</span><span class="tn">avg '+tmx.averageCognitiveComplexity+' / max '+tmx.maxCognitiveComplexity+'</span></div>';
      h+='<div class="tl"><span class="tk">CycCC</span><span class="tn">avg '+tmx.averageCyclomaticComplexity+' / max '+tmx.maxCyclomaticComplexity+'</span></div>';
      if(tmx.lcom!=null) h+='<div class="tl"><span class="tk">LCOM</span><span class="tn">'+(+tmx.lcom).toFixed(2)+'</span></div>';
      if(tmx.cbo!=null) h+='<div class="tl"><span class="tk"'+(tmx.cbo>=14?' style="color:#f97583"':'')+'>CBO</span><span class="tn">'+tmx.cbo+'</span></div>';
      if(tmx.dit!=null&&tmx.dit>0) h+='<div class="tl"><span class="tk"'+(tmx.dit>=6?' style="color:#f97583"':'')+'>DIT</span><span class="tn">'+tmx.dit+'</span></div>';
      if(tmx.instability!=null) h+='<div class="tl"><span class="tk">I</span><span class="tn">'+(+tmx.instability).toFixed(2)+'</span></div>';
      if(tmx.minMaintainabilityIndex!=null) h+='<div class="tl"><span class="tk"'+(tmx.minMaintainabilityIndex<20?' style="color:#f97583"':'')+'>MI</span><span class="tn">'+(+tmx.minMaintainabilityIndex).toFixed(1)+'</span></div>';
      if(tmx.codeSmells&&tmx.codeSmells.length) h+='<div class="tl"><span class="tk" style="color:#f97583">smells</span><span class="tn">'+tmx.codeSmells.length+'</span></div>';
    }
    if(t.baseType) h+='<div class="tl"><span class="tk">base</span><span class="tt">'+esc(t.baseType)+'</span></div>';
    if(t.interfaces&&t.interfaces.length)
      h+='<div class="tl"><span class="tk">impl</span><span class="tt">'+esc(t.interfaces.join(', '))+'</span></div>';
    const refs=DATA.dependencies.filter(x=>depFromId(x)===tk);
    const usages=DATA.dependencies.filter(x=>depToId(x)===tk);
    if(refs.length){
      h+='<div class="ts">References ('+refs.length+')</div>';
      const grouped={};refs.forEach(r=>{
        const key=depToId(r);
        grouped[key]=(grouped[key]||0)+1;
      });
      Object.entries(grouped).slice(0,8).forEach(([n,c])=>{
        h+='<div class="tl"><span class="tt">'+esc((tl[n]&&tl[n].name)||n)+'</span> <span class="tk">x'+c+'</span></div>';
      });
      if(Object.keys(grouped).length>8) h+='<div class="tl"><span class="tk">+'+(Object.keys(grouped).length-8)+' more</span></div>';
    }
    if(usages.length){
      h+='<div class="ts">Usages ('+usages.length+')</div>';
      const grouped={};usages.forEach(r=>{
        const key=depFromId(r);
        grouped[key]=(grouped[key]||0)+1;
      });
      Object.entries(grouped).slice(0,6).forEach(([n,c])=>{
        h+='<div class="tl"><span class="tt">'+esc((tl[n]&&tl[n].name)||n)+'</span> <span class="tk">x'+c+'</span></div>';
      });
      if(Object.keys(grouped).length>6) h+='<div class="tl"><span class="tk">+'+(Object.keys(grouped).length-6)+' more</span></div>';
    }
  } else {
    const ns=d.fullLabel;
    const tn=nsTree.get(ns);
    if(!tn){tip.classList.remove('show');return}
    h+='<h3 style="color:'+(tn.color||'#6e7681')+'">'+esc(ns)+'</h3>';
    h+='<div class="tm">';
    if(tn.virtual) h+='virtual &middot; ';
    h+=tn.descendantTypeCount+' types ('+tn.directTypeCount+' direct)</div>';
    if(tn.children.size){
      tn.children.forEach(ch=>{
        const cn=nsTree.get(ch);
        h+='<div class="tl"><span class="tk">NS</span><span class="tn">'+esc(cn.shortName)+' ('+cn.descendantTypeCount+')</span></div>';
      });
    }
    if(tn.directTypeCount){
      const types=DATA.types.filter(t=>(t.namespace||'(global)')===ns);
      types.slice(0,8).forEach(t=>{
        h+='<div class="tl"><span class="tk">'+t.kind[0].toUpperCase()+'</span><span class="tn">'+esc(t.name)+'</span></div>';
      });
      if(types.length>8) h+='<div class="tl"><span class="tk">+'+(types.length-8)+' more</span></div>';
    }
  }
  tip.innerHTML=h;
  const bw=document.body.clientWidth,bh=document.body.clientHeight;
  const tw=tip.offsetWidth||280,th=tip.offsetHeight||200;
  let tx=px+12,ty=py+12;
  if(tx+tw>bw-10)tx=px-tw-12;
  if(ty+th>bh-10)ty=py-th-12;
  tip.style.left=tx+'px';tip.style.top=ty+'px';
  tip.classList.add('show');
}

cy.on('mouseover','node',e=>{
  const rp=e.target.renderedPosition();
  const cr=document.getElementById('cy').getBoundingClientRect();
  showTip(e.target,cr.left+rp.x,cr.top+rp.y);
});
cy.on('mouseout','node',()=>{tip.classList.remove('show');tipNode=null});
cy.on('mousemove','node',e=>{
  if(!tipNode)return;
  const rp=e.target.renderedPosition();
  const cr=document.getElementById('cy').getBoundingClientRect();
  const px=cr.left+rp.x,py=cr.top+rp.y;
  const bw=document.body.clientWidth,bh=document.body.clientHeight;
  const tw=tip.offsetWidth||280,th=tip.offsetHeight||200;
  let tx=px+12,ty=py+12;
  if(tx+tw>bw-10)tx=px-tw-12;
  if(ty+th>bh-10)ty=py-th-12;
  tip.style.left=tx+'px';tip.style.top=ty+'px';
});
// --- Edge hover: highlight hovered edge + connected nodes only ---
cy.on('mouseover','edge',e=>{
  const edge=e.target;
  edge.style({'opacity':1,'width':edge.data('w')+1.5,'z-index':999});
  edge.connectedNodes().style({'border-width':2.5,'border-color':'#58a6ff'});
});
cy.on('mouseout','edge',e=>{
  e.target.removeStyle('opacity width z-index');
  e.target.connectedNodes().removeStyle('border-width border-color');
});

// --- Shared utility ---
const escAttr=s=>esc(s).replace(/"/g,'&quot;');

// --- navigateToType ---
function navigateToType(typeName){
  const t=tl[typeName]; if(!t) return;
  const ns=t.namespace||'(global)';
  const parts=ns.split('.');
  for(let i=1;i<=parts.length;i++) expanded.add(parts.slice(0,i).join('.'));
  rebuild();layout();
  cy.one('layoutstop',()=>{
    const node=cy.getElementById('t:'+typeName);
    if(node.empty()) return;
    cy.animate({fit:{eles:node,padding:120},duration:300});
    node.addClass('hl');
    setTimeout(()=>node.removeClass('hl'),2000);
  });
}

// --- Feature 1: Hotspot Panel ---
let hotMethods=null,hotTypes=null;
function buildHotspots(){
  if(hotMethods) return;
  hotMethods=[];
  (DATA.typeMetrics||[]).forEach(t=>{
    (t.methods||[]).forEach(m=>{
      hotMethods.push({
        methodName:m.name,typeName:t.typeName,typeId:metricKey(t),
        cogcc:m.cognitiveComplexity||0,cyccc:m.cyclomaticComplexity||0,
        loc:m.lineCount||0,nest:m.maxNestingDepth||0
      });
    });
  });
  hotTypes=(DATA.typeMetrics||[]).map(t=>({
    typeName:t.typeName,typeId:metricKey(t),health:t.codeHealth!=null?t.codeHealth:10,
    cogcc:t.maxCognitiveComplexity||0,cyccc:t.maxCyclomaticComplexity||0,
    loc:t.lineCount||0,nest:t.maxNestingDepth||0,
    lcom:t.lcom,cbo:t.cbo,dit:t.dit,mi:t.minMaintainabilityIndex,
    instability:t.instability,smells:(t.codeSmells||[]).length
  }));
}

function renderHotspots(tab,sortKey){
  buildHotspots();
  const list=document.getElementById('hpList');
  let items=tab==='methods'?hotMethods:hotTypes;
  let sk=sortKey;
  if(tab==='methods'&&sk==='health') sk='cogcc';
  const sorters={
    cogcc:(a,b)=>b.cogcc-a.cogcc,cyccc:(a,b)=>b.cyccc-a.cyccc,
    loc:(a,b)=>b.loc-a.loc,nest:(a,b)=>b.nest-a.nest,
    health:(a,b)=>a.health-b.health,
    cbo:(a,b)=>(b.cbo||0)-(a.cbo||0),
    mi:(a,b)=>(a.mi||100)-(b.mi||100),
    smells:(a,b)=>b.smells-a.smells
  };
  items=[...items].sort(sorters[sk]||sorters.cogcc).slice(0,20);
  let h='';
  items.forEach(it=>{
    const name=tab==='methods'?it.methodName:it.typeName;
    const sub=tab==='methods'?it.typeName:'';
    h+='<div class="hp-item" data-type="'+escAttr(it.typeId)+'">';
    h+='<div class="hp-name">'+esc(name)+'</div>';
    if(sub) h+='<div class="hp-sub">'+esc(sub)+'</div>';
    h+='<div class="hp-badges">';
    h+='<span class="badge" style="color:'+(it.cogcc>15?'#f97583':it.cogcc>8?'#e3b341':'#7ee787')+'">CogCC:'+it.cogcc+'</span>';
    h+='<span class="badge" style="color:'+(it.cyccc>10?'#f97583':it.cyccc>5?'#e3b341':'#7ee787')+'">CycCC:'+it.cyccc+'</span>';
    h+='<span class="badge">'+it.loc+'L</span>';
    h+='<span class="badge">D:'+it.nest+'</span>';
    if(tab==='types'&&it.health!=null) h+='<span class="badge" style="color:'+healthColor(it.health)+'">H:'+it.health+'</span>';
    if(tab==='types'&&it.cbo!=null) h+='<span class="badge"'+(it.cbo>=14?' style="color:#f97583"':'')+'>CBO:'+it.cbo+'</span>';
    if(tab==='types'&&it.smells>0) h+='<span class="badge" style="color:#f97583">S:'+it.smells+'</span>';
    h+='</div></div>';
  });
  if(!items.length) h='<div style="padding:12px;color:var(--dim)">No data</div>';
  list.innerHTML=h;
}

// --- Feature 2: Cycle Detection (Tarjan SCC) ---
let cycles=[];

function buildAdjList(){
  const adj=new Map();
  DATA.types.forEach(t=>adj.set(typeKey(t),[]));
  (DATA.dependencies||[]).forEach(d=>{
    const fromId=depFromId(d), toId=depToId(d);
    if(adj.has(fromId)&&adj.has(toId)) adj.get(fromId).push(toId);
  });
  return adj;
}

function tarjanSCC(adj){
  let idx=0;const stack=[],on=new Set();
  const ix=new Map(),lw=new Map(),sccs=[];
  function sc(v){
    ix.set(v,idx);lw.set(v,idx);idx++;
    stack.push(v);on.add(v);
    (adj.get(v)||[]).forEach(w=>{
      if(!ix.has(w)){sc(w);lw.set(v,Math.min(lw.get(v),lw.get(w)));}
      else if(on.has(w)) lw.set(v,Math.min(lw.get(v),ix.get(w)));
    });
    if(lw.get(v)===ix.get(v)){
      const scc=[];let w;
      do{w=stack.pop();on.delete(w);scc.push(w);}while(w!==v);
      if(scc.length>1) sccs.push(scc);
    }
  }
  adj.forEach((_,v)=>{if(!ix.has(v)) sc(v);});
  return sccs;
}

function detectCycles(){
  cycles=tarjanSCC(buildAdjList());
  const btn=document.getElementById('bCyc');
  btn.innerHTML=cycles.length>0
    ?'Cycles <span class="cnt">'+cycles.length+'</span>'
    :'Cycles';
}

function renderCyclePanel(){
  const list=document.getElementById('cycList');
  if(!cycles.length){
    list.innerHTML='<div style="padding:12px;color:var(--dim)">No circular dependencies detected</div>';
    return;
  }
  let h='';
  cycles.forEach((scc,i)=>{
    h+='<div class="cyc-group"><div class="cyc-title" data-idx="'+i+'">Cycle '+(i+1)+' ('+scc.length+' types)</div>';
    scc.forEach(t=>{
      const info=tl[t];
      h+='<div class="cyc-item" data-type="'+escAttr(t)+'">'+esc(info?info.name:t)+'</div>';
    });
    h+='</div>';
  });
  list.innerHTML=h;
}

function highlightCycle(i){
  clearCycleHighlight();
  if(i<0||i>=cycles.length) return;
  const scc=new Set(cycles[i]);
  scc.forEach(tn=>{
    const t=tl[tn];if(!t) return;
    const ns=t.namespace||'(global)';
    const parts=ns.split('.');
    for(let j=1;j<=parts.length;j++) expanded.add(parts.slice(0,j).join('.'));
  });
  rebuild();layout();
  cy.one('layoutstop',()=>{
    cy.elements().addClass('dim');
    scc.forEach(tn=>{
      const n=cy.getElementById('t:'+tn);
      if(!n.empty()) n.removeClass('dim').addClass('cycle');
    });
    cy.edges().forEach(e=>{
      if(!e.data('meta')&&scc.has(e.source().data('typeId'))&&scc.has(e.target().data('typeId')))
        e.removeClass('dim').addClass('cycle-edge');
    });
    const cn=cy.nodes('.cycle');
    if(cn.length) cy.animate({fit:{eles:cn,padding:80},duration:300});
  });
}

function clearCycleHighlight(){
  cy.elements().removeClass('dim cycle cycle-edge');
}

// --- Feature 3: Assembly Dependency Summary ---
function computeAsmDeps(){
  const names=[...new Set(DATA.types.map(t=>t.assembly))];
  const mx=new Map();
  names.forEach(a=>{const r=new Map();names.forEach(b=>r.set(b,0));mx.set(a,r);});
  (DATA.dependencies||[]).forEach(d=>{
    const f=tl[depFromId(d)],t2=tl[depToId(d)];
    if(!f||!t2||f.assembly===t2.assembly) return;
    mx.get(f.assembly).set(t2.assembly,mx.get(f.assembly).get(t2.assembly)+1);
  });
  return {names,mx};
}

function computeCoupling(deps){
  const {names,mx}=deps;
  return names.map(a=>{
    let ca=0,ce=0;
    names.forEach(b=>{
      if(a===b) return;
      if(mx.get(b).get(a)>0) ca++;
      if(mx.get(a).get(b)>0) ce++;
    });
    return {name:a,ca,ce,inst:ca+ce>0?ce/(ca+ce):0};
  });
}

function renderAsmModal(){
  const deps=computeAsmDeps();
  const coup=computeCoupling(deps);
  const {names,mx}=deps;
  const sh=n=>n.split('.').pop();
  let h='<h2>Assembly Dependencies</h2><h3>Dependency Matrix</h3>';
  h+='<div style="overflow:auto;max-height:50vh"><table class="asm-table"><tr><th></th>';
  names.forEach(n=>h+='<th title="'+escAttr(n)+'">'+esc(sh(n))+'</th>');
  h+='</tr>';
  names.forEach(from=>{
    h+='<tr><th title="'+escAttr(from)+'">'+esc(sh(from))+'</th>';
    names.forEach(to=>{
      if(from===to){h+='<td class="diag">-</td>';return;}
      const v=mx.get(from).get(to);
      h+='<td class="'+(v===0?'c0':v<=3?'c1':v<=10?'c2':'c3')+'">'+(v||'')+'</td>';
    });
    h+='</tr>';
  });
  h+='</table></div>';
  h+='<h3>Coupling Metrics</h3>';
  h+='<table class="coup-table"><tr><th>Assembly</th><th>Ca</th><th>Ce</th><th>Instability</th></tr>';
  coup.sort((a,b)=>b.inst-a.inst).forEach(c=>{
    h+='<tr><td>'+esc(sh(c.name))+'</td><td>'+c.ca+'</td><td>'+c.ce+'</td>';
    h+='<td'+(c.inst>0.7?' class="hi"':'')+'>'+c.inst.toFixed(2)+'</td></tr>';
  });
  h+='</table>';
  document.getElementById('asmContent').innerHTML=h;
}

// --- Panel toggle ---
function togglePanel(id,btnId){
  const panels=['hp','cycp'],btns=['bHot','bCyc'];
  const el=document.getElementById(id);
  const isOpen=!el.classList.contains('hidden');
  panels.forEach((p,j)=>{
    document.getElementById(p).classList.add('hidden');
    document.getElementById(btns[j]).classList.remove('active');
  });
  clearCycleHighlight();
  if(!isOpen){
    el.classList.remove('hidden');
    document.getElementById(btnId).classList.add('active');
  }
}

// --- Init features ---
detectCycles();
if(cycles.length){
  stText+=' \u00b7 '+cycles.length+' cycle'+(cycles.length>1?'s':'');
  document.getElementById('st').textContent=stText;
}

document.getElementById('bHot').onclick=()=>{
  togglePanel('hp','bHot');
  if(!document.getElementById('hp').classList.contains('hidden')){
    const tab=document.querySelector('.lp-tab.active[data-panel="hp"]')?.dataset.tab||'methods';
    renderHotspots(tab,document.getElementById('hpSort').value);
  }
};
document.getElementById('bCyc').onclick=()=>{
  togglePanel('cycp','bCyc');
  if(!document.getElementById('cycp').classList.contains('hidden')) renderCyclePanel();
};
document.getElementById('bAsm').onclick=()=>{
  const ov=document.getElementById('asmOv');
  if(ov.classList.contains('hidden')){renderAsmModal();ov.classList.remove('hidden');}
  else ov.classList.add('hidden');
};
document.getElementById('asmOv').onclick=e=>{
  if(e.target.id==='asmOv') document.getElementById('asmOv').classList.add('hidden');
};

document.querySelectorAll('.lp-tab[data-panel="hp"]').forEach(tab=>{
  tab.onclick=()=>{
    document.querySelectorAll('.lp-tab[data-panel="hp"]').forEach(t=>t.classList.remove('active'));
    tab.classList.add('active');
    renderHotspots(tab.dataset.tab,document.getElementById('hpSort').value);
  };
});
document.getElementById('hpSort').onchange=function(){
  const tab=document.querySelector('.lp-tab.active[data-panel="hp"]')?.dataset.tab||'methods';
  renderHotspots(tab,this.value);
};

document.getElementById('hpList').addEventListener('click',e=>{
  const item=e.target.closest('.hp-item');
  if(item&&item.dataset.type) navigateToType(item.dataset.type);
});
document.getElementById('cycList').addEventListener('click',e=>{
  const title=e.target.closest('.cyc-title');
  if(title){highlightCycle(parseInt(title.dataset.idx));return;}
  const item=e.target.closest('.cyc-item');
  if(item&&item.dataset.type) navigateToType(item.dataset.type);
});

}); // end document.fonts.ready
</script>
</body>
</html>
""";
}
