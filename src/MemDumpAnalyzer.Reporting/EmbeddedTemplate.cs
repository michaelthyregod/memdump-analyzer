namespace MemDumpAnalyzer.Reporting;

/// <summary>Holds the Scriban HTML template as a string literal to avoid resource embedding complexity.</summary>
internal static class EmbeddedTemplate
{
    internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Memory Dump Analysis — {{ r.dump_path | string.split '\\' | array.last }}</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f7fa;color:#1a1a2e;line-height:1.6}
.container{max-width:1200px;margin:0 auto;padding:2rem}
h1{font-size:2rem;margin-bottom:.25rem;color:#0d1b2a}
h2{font-size:1.4rem;margin:2rem 0 1rem;color:#1b4f72;border-bottom:2px solid #d6e4f0;padding-bottom:.4rem}
h3{font-size:1.1rem;margin:1.5rem 0 .5rem}
.meta-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1rem;margin:1rem 0}
.meta-card{background:#fff;border-radius:8px;padding:1rem;box-shadow:0 1px 4px rgba(0,0,0,.08)}
.meta-card .label{font-size:.75rem;text-transform:uppercase;letter-spacing:.05em;color:#666;margin-bottom:.25rem}
.meta-card .value{font-size:1.1rem;font-weight:600;color:#0d1b2a}
.health-score{font-size:2rem;font-weight:700}
.health-high{color:#27ae60}.health-med{color:#f39c12}.health-low{color:#e74c3c}
.findings{margin:1rem 0}
.finding{background:#fff;border-radius:8px;padding:1rem 1.25rem;margin:.5rem 0;border-left:4px solid #ccc;box-shadow:0 1px 3px rgba(0,0,0,.06)}
.finding.critical{border-color:#e74c3c;background:#fff5f5}
.finding.warning{border-color:#f39c12;background:#fffbf0}
.finding.info{border-color:#3498db;background:#f0f7ff}
.badge{display:inline-block;padding:.15rem .55rem;border-radius:4px;font-size:.75rem;font-weight:700;text-transform:uppercase;margin-right:.5rem}
.badge.critical{background:#e74c3c;color:#fff}
.badge.warning{background:#f39c12;color:#fff}
.badge.info{background:#3498db;color:#fff}
.finding-summary{font-weight:600;margin-bottom:.3rem}
.finding-detail{font-size:.9rem;color:#555;margin:.2rem 0}
table{width:100%;border-collapse:collapse;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.07)}
th{background:#1b4f72;color:#fff;padding:.6rem .9rem;text-align:left;font-size:.85rem}
td{padding:.5rem .9rem;border-bottom:1px solid #eef1f5;font-size:.85rem}
tr:last-child td{border-bottom:none}
tr:nth-child(even) td{background:#f9fbfc}
.chart-wrap{background:#fff;border-radius:8px;padding:1.5rem;box-shadow:0 1px 3px rgba(0,0,0,.07);margin:1rem 0}
.bar-row{display:flex;align-items:center;margin:.35rem 0;font-size:.85rem}
.bar-label{width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:#444}
.bar-track{flex:1;background:#eef1f5;border-radius:4px;height:18px;margin:0 .75rem}
.bar-fill{height:100%;border-radius:4px;background:#1b4f72;min-width:2px}
.bar-value{width:90px;text-align:right;color:#555}
details summary{cursor:pointer;font-weight:600;padding:.5rem 0;color:#1b4f72}
details[open] summary{margin-bottom:.75rem}
.stack{font-family:'Cascadia Code','Fira Code',monospace;font-size:.78rem;background:#f4f4f8;border-radius:4px;padding:.5rem;max-height:200px;overflow-y:auto}
.tech-details{font-family:'Cascadia Code','Fira Code','Consolas',monospace;font-size:.78rem;background:#0d1b2a;color:#c8d8e8;border-radius:6px;padding:1rem;margin-top:.75rem;white-space:pre;overflow-x:auto;max-height:500px;overflow-y:auto;line-height:1.5}
.tech-details summary{font-family:inherit;font-size:.8rem;color:#7fb3d3;cursor:pointer;padding:.25rem 0}
.section{background:#fff;border-radius:8px;padding:1.5rem;margin:1rem 0;box-shadow:0 1px 3px rgba(0,0,0,.07)}
footer{margin-top:3rem;text-align:center;font-size:.8rem;color:#999}
</style>
</head>
<body>
<div class="container">

<h1>Memory Dump Analysis</h1>
<p style="color:#666;margin-bottom:1.5rem">Generated {{ date.now | date.to_string '%Y-%m-%d %H:%M' }} UTC</p>

<!-- Executive Summary -->
<div class="meta-grid">
  <div class="meta-card">
    <div class="label">Dump file</div>
    <div class="value" style="font-size:.9rem;word-break:break-all">{{ r.dump_path | string.split '\\' | array.last }}</div>
  </div>
  <div class="meta-card">
    <div class="label">Capture time</div>
    <div class="value">{{ r.capture_time | date.to_string '%Y-%m-%d %H:%M' }}</div>
  </div>
  <div class="meta-card">
    <div class="label">Dump size</div>
    <div class="value">{{ format_bytes r.dump_file_size_bytes }}</div>
  </div>
  <div class="meta-card">
    <div class="label">CLR version</div>
    <div class="value">{{ r.clr_version }}</div>
  </div>
  <div class="meta-card">
    <div class="label">AppDomain</div>
    <div class="value">{{ r.app_domain_name }}</div>
  </div>
  <div class="meta-card">
    <div class="label">Health Score</div>
    <div class="value health-score {{ if r.health_score >= 70 }}health-high{{ else if r.health_score >= 40 }}health-med{{ else }}health-low{{ end }}">{{ r.health_score }}/100</div>
  </div>
</div>

<!-- Findings -->
<h2>Findings</h2>
<div class="findings">
{{ for f in r.findings }}
  <div class="finding {{ severity_class f.severity }}">
    <div class="finding-summary"><span class="badge {{ severity_class f.severity }}">{{ severity_label f.severity }}</span>{{ f.summary }}</div>
    <div class="finding-detail">{{ f.explanation }}</div>
    {{ if f.evidence }}<div class="finding-detail"><strong>Evidence:</strong> {{ f.evidence }}</div>{{ end }}
    {{ if f.recommendation }}<div class="finding-detail" style="color:#1a5276"><strong>Recommendation:</strong> {{ f.recommendation }}</div>{{ end }}
    {{ if f.technical_details }}
    <details class="tech-details-wrap" style="margin-top:.5rem">
      <summary class="tech-details" style="background:#1a2a3a;padding:.4rem .75rem;border-radius:4px 4px 0 0">▶ Technical details (click to expand)</summary>
      <div class="tech-details" style="border-radius:0 0 6px 6px;margin-top:0">{{ for line in f.technical_details }}{{ line }}
{{ end }}</div>
    </details>
    {{ end }}
  </div>
{{ end }}
{{ if r.findings.size == 0 }}<p style="color:#27ae60;font-weight:600">No significant findings — heap looks healthy!</p>{{ end }}
</div>

<!-- Memory Overview -->
<h2>Memory Overview</h2>
<div class="chart-wrap">
{{ max_bytes = r.gc_stats.total_heap_bytes > 0 ? r.gc_stats.total_heap_bytes : 1 }}
{{ for row in [['Gen0', r.gc_stats.gen0_bytes], ['Gen1', r.gc_stats.gen1_bytes], ['Gen2', r.gc_stats.gen2_bytes], ['LOH', r.gc_stats.loh_bytes], ['POH', r.gc_stats.poh_bytes]] }}
  <div class="bar-row">
    <div class="bar-label">{{ row[0] }}</div>
    <div class="bar-track"><div class="bar-fill" style="width:{{ math.floor(row[1] * 100 / max_bytes) }}%"></div></div>
    <div class="bar-value">{{ format_bytes row[1] }}</div>
  </div>
{{ end }}
<div style="margin-top:.75rem;font-size:.85rem;color:#555">Total heap: <strong>{{ format_bytes r.gc_stats.total_heap_bytes }}</strong> &nbsp;|&nbsp; LOH fragmentation: <strong>{{ math.round r.gc_stats.loh_fragmentation_percent 1 }}%</strong></div>
</div>

<!-- Top Memory Consumers -->
<h2>Top Memory Consumers</h2>
{{ top_size = (r.heap_types.size > 0 && r.heap_types[0].total_size_bytes > 0) ? r.heap_types[0].total_size_bytes : 1 }}
<div class="chart-wrap" style="margin-bottom:1rem">
{{ for t in r.heap_types | array.limit 15 }}
  <div class="bar-row">
    <div class="bar-label" title="{{ t.type_name }}">{{ t.type_name }}</div>
    <div class="bar-track"><div class="bar-fill" style="width:{{ math.floor(t.total_size_bytes * 100 / top_size) }}%"></div></div>
    <div class="bar-value">{{ format_bytes t.total_size_bytes }}</div>
  </div>
{{ end }}
</div>
<details>
  <summary>Full type table ({{ r.heap_types.size }} types)</summary>
  <table>
    <thead><tr><th>Type</th><th>Instances</th><th>Total Size</th><th>Gen</th></tr></thead>
    <tbody>
    {{ for t in r.heap_types | array.limit 500 }}
      <tr><td style="font-family:monospace;font-size:.8rem">{{ t.type_name }}</td><td style="text-align:right">{{ t.instance_count }}</td><td style="text-align:right">{{ format_bytes t.total_size_bytes }}</td><td style="text-align:right">{{ t.generation }}</td></tr>
    {{ end }}
    </tbody>
  </table>
</details>

<!-- Thread Analysis -->
<h2>Thread Analysis</h2>
<div class="section" style="padding:1rem">
  <p>Total threads: <strong>{{ r.threads.size }}</strong> &nbsp;|&nbsp; Blocked: <strong>{{ blocked_thread_count }}</strong></p>
</div>
<details>
  <summary>Thread list ({{ r.threads.size }} threads)</summary>
  <table>
    <thead><tr><th>OS TID</th><th>Mgd TID</th><th>State</th><th>Exception</th><th>Stack (top 5)</th></tr></thead>
    <tbody>
    {{ for t in r.threads | array.limit 300 }}
      <tr>
        <td>{{ t.os_thread_id }}</td>
        <td>{{ t.managed_thread_id }}</td>
        <td>{{ t.state }}</td>
        <td style="font-size:.8rem;color:#c0392b">{{ t.current_exception }}</td>
        <td><div class="stack">{{ for f in t.stack_frames | array.limit 5 }}{{ f }}&#10;{{ end }}</div></td>
      </tr>
    {{ end }}
    {{ if r.threads.size > 300 }}<tr><td colspan="5" style="color:#888;font-style:italic">… {{ r.threads.size - 300 }} more threads not shown</td></tr>{{ end }}
    </tbody>
  </table>
</details>

<!-- GC Health -->
<h2>GC Health</h2>
<table>
  <thead><tr><th>Kind</th><th>Committed</th><th>Reserved</th><th>Fill %</th></tr></thead>
  <tbody>
  {{ for seg in r.gc_stats.segments | array.limit 200 }}
    <tr>
      <td>{{ seg.kind }}</td>
      <td style="text-align:right">{{ format_bytes seg.committed_bytes }}</td>
      <td style="text-align:right">{{ format_bytes seg.reserved_bytes }}</td>
      <td style="text-align:right">{{ math.round(seg.fill_ratio * 100, 1) }}%</td>
    </tr>
  {{ end }}
  </tbody>
</table>

<!-- Duplicate Strings -->
{{ if r.duplicate_strings.size > 0 }}
<h2>Duplicate Strings</h2>
<details>
  <summary>{{ r.duplicate_strings.size }} duplicate string groups</summary>
  <table>
    <thead><tr><th>Value (truncated)</th><th>Instances</th><th>Wasted</th></tr></thead>
    <tbody>
    {{ for s in r.duplicate_strings | array.limit 20 }}
      <tr><td style="font-family:monospace;font-size:.8rem">{{ s.value }}</td><td style="text-align:right">{{ s.instance_count }}</td><td style="text-align:right">{{ format_bytes s.total_wasted_bytes }}</td></tr>
    {{ end }}
    </tbody>
  </table>
</details>
{{ end }}

<!-- Application Code Hotspots -->
{{ if r.application_hotspots.size > 0 }}
<h2>Application Code Hotspots</h2>
<p style="color:#555;font-size:.9rem;margin-bottom:1rem">
  Assemblies found in thread stacks that are <strong>not suppressed</strong> by the known-assembly filter
  ({{ r.known_assembly_filters.size }} rule{{ if r.known_assembly_filters.size != 1 }}s{{ end }} active —
  <code style="font-size:.8rem">-prefix</code> hides an assembly, <code style="font-size:.8rem">+prefix</code> forces it visible even under a broader exclude).
  These are likely your own application code contributing to the problems above.
</p>
<table>
  <thead><tr><th>Assembly</th><th style="text-align:right">Threads</th><th style="text-align:right">Blocked</th><th>Most frequent method</th></tr></thead>
  <tbody>
  {{ for h in r.application_hotspots | array.limit 30 }}
    <tr>
      <td style="font-family:monospace;font-weight:600;font-size:.85rem">{{ h.assembly }}</td>
      <td style="text-align:right">{{ h.thread_count }}</td>
      <td style="text-align:right{{ if h.blocked_thread_count > 0 }};color:#e74c3c;font-weight:700{{ end }}">{{ h.blocked_thread_count }}</td>
      <td style="font-family:monospace;font-size:.78rem;color:#444">{{ h.top_methods | array.first }}</td>
    </tr>
  {{ end }}
  </tbody>
</table>
<details style="margin-top:1rem">
  <summary>All methods per assembly (click to expand)</summary>
  {{ for h in r.application_hotspots | array.limit 30 }}
  <h3 style="margin:1.25rem 0 .4rem;color:#1b4f72;font-size:1rem">{{ h.assembly }}
    <span style="font-weight:400;font-size:.85rem;color:#666"> — {{ h.thread_count }} thread{{ if h.thread_count != 1 }}s{{ end }}{{ if h.blocked_thread_count > 0 }}, <span style="color:#e74c3c">{{ h.blocked_thread_count }} blocked</span>{{ end }}</span>
  </h3>
  <div class="tech-details" style="background:#0d1b2a;color:#c8d8e8;padding:.75rem 1rem;border-radius:6px;font-size:.78rem;line-height:1.6">{{ for m in h.top_methods }}{{ m }}
{{ end }}</div>
  {{ end }}
</details>
{{ end }}

<!-- Findings Detail -->
<h2>Findings Detail</h2>
{{ for f in r.findings }}
<div class="finding {{ severity_class f.severity }}" style="margin:.75rem 0">
  <h3><span class="badge {{ severity_class f.severity }}">{{ severity_label f.severity }}</span>{{ f.id }}</h3>
  <p style="font-weight:600;margin:.4rem 0">{{ f.summary }}</p>
  <p style="color:#555;font-size:.9rem">{{ f.explanation }}</p>
  {{ if f.evidence }}<p style="font-size:.85rem;margin-top:.4rem"><strong>Evidence:</strong> {{ f.evidence }}</p>{{ end }}
  {{ if f.recommendation }}<p style="font-size:.85rem;margin-top:.4rem;color:#1a5276"><strong>Recommendation:</strong> {{ f.recommendation }}</p>{{ end }}
  {{ if f.technical_details }}
  <details style="margin-top:.75rem">
    <summary class="tech-details" style="background:#1a2a3a;padding:.4rem .75rem;border-radius:4px;color:#7fb3d3;cursor:pointer">▶ Technical details</summary>
    <div class="tech-details" style="margin-top:4px">{{ for line in f.technical_details }}{{ line }}
{{ end }}</div>
  </details>
  {{ end }}
</div>
{{ end }}

<footer>memdump-analyzer &bull; {{ date.now | date.to_string '%Y' }}</footer>
</div>
</body>
</html>
""";
}
