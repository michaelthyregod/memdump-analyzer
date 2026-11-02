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
<title>{{ skin_title | html.escape }} — {{ dump_file_name | html.escape }}</title>
<style>
{{ skin_css }}
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:var(--font-body);background:var(--background);color:var(--text);line-height:1.6}
h1,h2,h3{font-family:var(--font-heading);font-weight:var(--heading-weight)}
.container{max-width:1200px;margin:0 auto;padding:2rem}
.report-header{display:flex;align-items:center;gap:var(--logo-clearance);margin-bottom:.25rem}
.report-header img{height:var(--logo-height);width:auto;margin:var(--logo-clearance) 0}
h1{font-size:2rem;color:var(--heading)}
.company{color:var(--muted);font-size:.95rem}
h2{font-size:1.4rem;margin:2rem 0 1rem;color:var(--primary);border-bottom:2px solid var(--border);padding-bottom:.4rem}
h3{font-size:1.1rem;margin:1.5rem 0 .5rem}
.meta-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1rem;margin:1rem 0}
.meta-card{background:var(--surface);border-radius:8px;padding:1rem;box-shadow:0 1px 4px rgba(0,0,0,.08)}
.meta-card .label{font-size:.75rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:.25rem}
.meta-card .value{font-size:1.1rem;font-weight:600;color:var(--heading)}
.health-score{font-size:2rem;font-weight:700}
.health-high{color:var(--success)}.health-med{color:var(--warning)}.health-low{color:var(--critical)}
.findings{margin:1rem 0}
.finding{background:var(--surface);border-radius:8px;padding:1rem 1.25rem;margin:.5rem 0;border-left:4px solid var(--border);box-shadow:0 1px 3px rgba(0,0,0,.06)}
.finding.critical{border-color:var(--critical);background:var(--critical-background)}
.finding.warning{border-color:var(--warning);background:var(--warning-background)}
.finding.info{border-color:var(--info);background:var(--info-background)}
.badge{display:inline-block;padding:.15rem .55rem;border-radius:4px;font-size:.75rem;font-weight:700;text-transform:uppercase;margin-right:.5rem;color:var(--on-primary)}
.badge.critical{background:var(--critical)}
.badge.warning{background:var(--warning)}
.badge.info{background:var(--info)}
.finding-summary{font-weight:600;margin-bottom:.3rem}
.finding-detail{font-size:.9rem;color:var(--muted);margin:.2rem 0}
.recommendation{color:var(--accent)}
table{width:100%;border-collapse:collapse;background:var(--surface);border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.07)}
th{background:var(--primary);color:var(--on-primary);padding:.6rem .9rem;text-align:left;font-size:.85rem}
td{padding:.5rem .9rem;border-bottom:1px solid var(--divider);font-size:.85rem}
tr:last-child td{border-bottom:none}
tr:nth-child(even) td{background:var(--stripe)}
.chart-wrap{background:var(--surface);border-radius:8px;padding:1.5rem;box-shadow:0 1px 3px rgba(0,0,0,.07);margin:1rem 0}
.bar-row{display:flex;align-items:center;margin:.35rem 0;font-size:.85rem}
.bar-label{width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:var(--muted)}
.bar-track{flex:1;background:var(--divider);border-radius:4px;height:18px;margin:0 .75rem}
.bar-fill{height:100%;border-radius:4px;background:var(--primary);min-width:2px}
.bar-value{width:90px;text-align:right;color:var(--muted)}
details summary{cursor:pointer;font-weight:600;padding:.5rem 0;color:var(--primary)}
details[open] summary{margin-bottom:.75rem}
.stack{font-family:var(--font-mono);font-size:.78rem;background:var(--stack-background);border-radius:4px;padding:.5rem;max-height:200px;overflow-y:auto}
.tech-details{font-family:var(--font-mono);font-size:.78rem;background:var(--code-background);color:var(--code-text);border-radius:6px;padding:1rem;margin-top:.75rem;white-space:pre;overflow-x:auto;max-height:500px;overflow-y:auto;line-height:1.5}
.tech-details summary{font-family:inherit;font-size:.8rem;color:var(--code-accent);cursor:pointer;padding:.25rem 0}
.section{background:var(--surface);border-radius:8px;padding:1.5rem;margin:1rem 0;box-shadow:0 1px 3px rgba(0,0,0,.07)}
footer{margin-top:3rem;text-align:center;font-size:.8rem;color:var(--subtle)}
</style>
</head>
<body>
<div class="container">

<div class="report-header">
  {{ if skin_logo }}<img src="{{ skin_logo }}" alt="">{{ end }}
  <div>
    <h1>{{ skin_title | html.escape }}</h1>
    {{ if skin_company }}<div class="company">{{ skin_company | html.escape }}</div>{{ end }}
  </div>
</div>
<p style="color:var(--muted);margin-bottom:1.5rem">Generated {{ generated_utc }} UTC</p>

<!-- Executive Summary -->
<div class="meta-grid">
  <div class="meta-card">
    <div class="label">Dump file</div>
    <div class="value" style="font-size:.9rem;word-break:break-all">{{ dump_file_name | html.escape }}</div>
  </div>
  <div class="meta-card">
    <div class="label">Capture time</div>
    <div class="value">{{ r.capture_time | date.to_string '%Y-%m-%d %H:%M:%S' }} UTC</div>
  </div>
  <div class="meta-card">
    <div class="label">Dump size</div>
    <div class="value">{{ format_bytes r.dump_file_size_bytes }}</div>
  </div>
  <div class="meta-card">
    <div class="label">CLR version</div>
    <div class="value">{{ r.clr_version | html.escape }}</div>
  </div>
  <div class="meta-card">
    <div class="label">AppDomain</div>
    <div class="value">{{ r.app_domain_name | html.escape }}</div>
  </div>
  {{ if skin_prepared_for }}<div class="meta-card">
    <div class="label">Prepared for</div>
    <div class="value">{{ skin_prepared_for | html.escape }}</div>
  </div>{{ end }}
  {{ if skin_prepared_by }}<div class="meta-card">
    <div class="label">Prepared by</div>
    <div class="value">{{ skin_prepared_by | html.escape }}</div>
  </div>{{ end }}
  <div class="meta-card">
    <div class="label">Health Score</div>
    <div class="value health-score {{ if r.health_score >= 70 }}health-high{{ else if r.health_score >= 40 }}health-med{{ else }}health-low{{ end }}">{{ r.health_score }}/100</div>
  </div>
</div>

{{ if !r.heap_walk_complete }}
<div class="finding warning" style="margin:1rem 0"><strong>Incomplete heap data:</strong> the dump was captured while a garbage collection was running, so heap types, duplicate strings and fragmentation below come from the part of the heap that could be read.</div>
{{ end }}

<!-- Findings -->
<h2>{{ headings.findings | html.escape }}</h2>
<div class="findings">
{{ for f in r.findings }}
  <div class="finding {{ severity_class f.severity }}">
    <div class="finding-summary"><span class="badge {{ severity_class f.severity }}">{{ severity_label f.severity }}</span>{{ f.summary | html.escape }}</div>
    <div class="finding-detail">{{ f.explanation | html.escape }}</div>
    {{ if f.evidence }}<div class="finding-detail"><strong>Evidence:</strong> {{ f.evidence | html.escape }}</div>{{ end }}
    {{ if f.recommendation }}<div class="finding-detail" style="color:var(--accent)"><strong>Recommendation:</strong> {{ f.recommendation | html.escape }}</div>{{ end }}
    {{ if f.technical_details }}
    <details class="tech-details-wrap" style="margin-top:.5rem">
      <summary class="tech-details" style="background:var(--code-header);padding:.4rem .75rem;border-radius:4px 4px 0 0">Technical details (click to expand)</summary>
      <div class="tech-details" style="border-radius:0 0 6px 6px;margin-top:0">{{ for line in f.technical_details }}{{ line | html.escape }}
{{ end }}</div>
    </details>
    {{ end }}
  </div>
{{ end }}
{{ if r.findings.size == 0 }}<p style="color:var(--success);font-weight:600">No significant findings — heap looks healthy!</p>{{ end }}
</div>

<!-- Memory Overview -->
<h2>{{ headings.memory_overview | html.escape }}</h2>
<div class="chart-wrap">
{{ max_bytes = r.gc_stats.total_heap_bytes > 0 ? r.gc_stats.total_heap_bytes : 1 }}
{{ for row in [['Gen0', r.gc_stats.gen0_bytes], ['Gen1', r.gc_stats.gen1_bytes], ['Gen2', r.gc_stats.gen2_bytes], ['LOH', r.gc_stats.loh_bytes], ['POH', r.gc_stats.poh_bytes]] }}
  <div class="bar-row">
    <div class="bar-label">{{ row[0] }}</div>
    <div class="bar-track"><div class="bar-fill" style="width:{{ math.floor(row[1] * 100 / max_bytes) }}%"></div></div>
    <div class="bar-value">{{ format_bytes row[1] }}</div>
  </div>
{{ end }}
<div style="margin-top:.75rem;font-size:.85rem;color:var(--muted)">Total heap: <strong>{{ format_bytes r.gc_stats.total_heap_bytes }}</strong> &nbsp;|&nbsp; LOH fragmentation: <strong>{{ math.round r.gc_stats.loh_fragmentation_percent 1 }}%</strong> ({{ format_bytes r.gc_stats.loh_free_bytes }} free) &nbsp;|&nbsp; Gen2 fragmentation: <strong>{{ math.round r.gc_stats.gen2_fragmentation_percent 1 }}%</strong> ({{ format_bytes r.gc_stats.gen2_free_bytes }} free)</div>
</div>

<!-- Top Memory Consumers -->
<h2>{{ headings.top_memory_consumers | html.escape }}</h2>
{{ top_size = (r.heap_types.size > 0 && r.heap_types[0].total_size_bytes > 0) ? r.heap_types[0].total_size_bytes : 1 }}
<div class="chart-wrap" style="margin-bottom:1rem">
{{ for t in r.heap_types | array.limit 15 }}
  <div class="bar-row">
    <div class="bar-label" title="{{ t.type_name | html.escape }}">{{ t.type_name | html.escape }}</div>
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
      <tr><td style="font-family:monospace;font-size:.8rem">{{ t.type_name | html.escape }}</td><td style="text-align:right">{{ t.instance_count }}</td><td style="text-align:right">{{ format_bytes t.total_size_bytes }}</td><td style="text-align:right">{{ t.generation }}</td></tr>
    {{ end }}
    </tbody>
  </table>
</details>

<!-- Thread Analysis -->
<h2>{{ headings.thread_analysis | html.escape }}</h2>
<div class="section" style="padding:1rem">
  <p>Live threads: <strong>{{ alive_thread_count }}</strong> &nbsp;|&nbsp; Dead (not analyzed): <strong>{{ r.threads.size - alive_thread_count }}</strong> &nbsp;|&nbsp; Waiting/blocked: <strong>{{ blocked_thread_count }}</strong></p>
  {{ if r.thread_pool }}
  <p>Thread pool: active <strong>{{ r.thread_pool.active_worker_threads }}</strong>, idle <strong>{{ r.thread_pool.idle_worker_threads }}</strong>, min {{ r.thread_pool.min_threads }}, max {{ r.thread_pool.max_threads }} &nbsp;|&nbsp; CPU at capture (system-wide): <strong>{{ r.thread_pool.cpu_utilization }}%</strong></p>
  {{ end }}
</div>
<h3>{{ headings.thread_patterns | html.escape }} (live threads grouped by top stack frames)</h3>
<table>
  <thead><tr><th style="text-align:right">Threads</th><th style="text-align:right">%</th><th>Waiting on</th><th>Representative stack (top 12)</th></tr></thead>
  <tbody>
  {{ for g in r.thread_groups }}
    <tr>
      <td style="text-align:right;font-weight:600">{{ g.thread_count }}</td>
      <td style="text-align:right">{{ math.round(g.percentage, 1) }}</td>
      <td>{{ g.wait_reason | html.escape }}</td>
      <td><div class="stack">{{ for fr in g.representative_stack | array.limit 12 }}{{ fr | html.escape }}&#10;{{ end }}</div></td>
    </tr>
  {{ end }}
  </tbody>
</table>
<details>
  <summary>Thread list ({{ alive_thread_count }} live threads)</summary>
  <table>
    <thead><tr><th>OS TID</th><th>Mgd TID</th><th>State</th><th>Exception</th><th>Stack (top 5)</th></tr></thead>
    <tbody>
    {{ for t in alive_threads | array.limit 300 }}
      <tr>
        <td>{{ t.os_thread_id }}</td>
        <td>{{ t.managed_thread_id }}</td>
        <td>{{ t.state | html.escape }}</td>
        <td style="font-size:.8rem;color:var(--critical)">{{ t.current_exception | html.escape }}</td>
        <td><div class="stack">{{ for f in t.stack_frames | array.limit 5 }}{{ f | html.escape }}&#10;{{ end }}</div></td>
      </tr>
    {{ end }}
    {{ if alive_thread_count > 300 }}<tr><td colspan="5" style="color:var(--subtle);font-style:italic">… {{ alive_thread_count - 300 }} more threads not shown</td></tr>{{ end }}
    </tbody>
  </table>
</details>

<!-- GC Health -->
<h2>{{ headings.gc_health | html.escape }}</h2>
<table>
  <thead><tr><th>Kind</th><th>Committed</th><th>Reserved</th><th>Free objects</th><th>Fill %</th></tr></thead>
  <tbody>
  {{ for seg in r.gc_stats.segments | array.limit 200 }}
    <tr>
      <td>{{ seg.kind }}</td>
      <td style="text-align:right">{{ format_bytes seg.committed_bytes }}</td>
      <td style="text-align:right">{{ format_bytes seg.reserved_bytes }}</td>
      <td style="text-align:right">{{ format_bytes seg.free_bytes }}</td>
      <td style="text-align:right">{{ math.round(seg.fill_ratio * 100, 1) }}%</td>
    </tr>
  {{ end }}
  </tbody>
</table>

<!-- Duplicate Strings -->
{{ if r.duplicate_strings.size > 0 }}
<h2>{{ headings.duplicate_strings | html.escape }}</h2>
<details>
  <summary>{{ r.duplicate_strings.size }} duplicate string groups</summary>
  <table>
    <thead><tr><th>Value (truncated)</th><th>Instances</th><th>Wasted</th></tr></thead>
    <tbody>
    {{ for s in r.duplicate_strings | array.limit 20 }}
      <tr><td style="font-family:monospace;font-size:.8rem">{{ s.value | html.escape }}</td><td style="text-align:right">{{ s.instance_count }}</td><td style="text-align:right">{{ format_bytes s.total_wasted_bytes }}</td></tr>
    {{ end }}
    </tbody>
  </table>
</details>
{{ end }}

<!-- Application Code Hotspots -->
{{ if r.application_hotspots.size > 0 }}
<h2>{{ headings.application_code_hotspots | html.escape }}</h2>
<p style="color:var(--muted);font-size:.9rem;margin-bottom:1rem">
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
      <td style="font-family:monospace;font-weight:600;font-size:.85rem">{{ h.assembly | html.escape }}</td>
      <td style="text-align:right">{{ h.thread_count }}</td>
      <td style="text-align:right{{ if h.blocked_thread_count > 0 }};color:var(--critical);font-weight:700{{ end }}">{{ h.blocked_thread_count }}</td>
      <td style="font-family:monospace;font-size:.78rem;color:var(--muted)">{{ h.top_methods | array.first | html.escape }}</td>
    </tr>
  {{ end }}
  </tbody>
</table>
<details style="margin-top:1rem">
  <summary>All methods per assembly (click to expand)</summary>
  {{ for h in r.application_hotspots | array.limit 30 }}
  <h3 style="margin:1.25rem 0 .4rem;color:var(--primary);font-size:1rem">{{ h.assembly | html.escape }}
    <span style="font-weight:400;font-size:.85rem;color:var(--muted)"> — {{ h.thread_count }} thread{{ if h.thread_count != 1 }}s{{ end }}{{ if h.blocked_thread_count > 0 }}, <span style="color:var(--critical)">{{ h.blocked_thread_count }} blocked</span>{{ end }}</span>
  </h3>
  <div class="tech-details" style="background:var(--code-background);color:var(--code-text);padding:.75rem 1rem;border-radius:6px;font-size:.78rem;line-height:1.6">{{ for m in h.top_methods }}{{ m | html.escape }}
{{ end }}</div>
  {{ end }}
</details>
{{ end }}

<!-- Findings Detail -->
<h2>{{ headings.findings_detail | html.escape }}</h2>
{{ for f in r.findings }}
<div class="finding {{ severity_class f.severity }}" style="margin:.75rem 0">
  <h3><span class="badge {{ severity_class f.severity }}">{{ severity_label f.severity }}</span>{{ f.id | html.escape }}</h3>
  <p style="font-weight:600;margin:.4rem 0">{{ f.summary | html.escape }}</p>
  <p style="color:var(--muted);font-size:.9rem">{{ f.explanation | html.escape }}</p>
  {{ if f.evidence }}<p style="font-size:.85rem;margin-top:.4rem"><strong>Evidence:</strong> {{ f.evidence | html.escape }}</p>{{ end }}
  {{ if f.recommendation }}<p style="font-size:.85rem;margin-top:.4rem;color:var(--accent)"><strong>Recommendation:</strong> {{ f.recommendation | html.escape }}</p>{{ end }}
  {{ if f.technical_details }}
  <details style="margin-top:.75rem">
    <summary class="tech-details" style="background:var(--code-header);padding:.4rem .75rem;border-radius:4px;color:var(--code-accent);cursor:pointer">Technical details</summary>
    <div class="tech-details" style="margin-top:4px">{{ for line in f.technical_details }}{{ line | html.escape }}
{{ end }}</div>
  </details>
  {{ end }}
</div>
{{ end }}

<footer>{{ skin_footer | html.escape }}</footer>
</div>
</body>
</html>
""";
}
