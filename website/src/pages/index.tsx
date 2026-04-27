import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';

function Hero() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header style={{padding: '4rem 0', textAlign: 'center'}}>
      <div className="container">
        <h1 style={{fontSize: '3rem'}}>{siteConfig.title}</h1>
        <p style={{fontSize: '1.25rem', fontStyle: 'italic', color: 'var(--ifm-color-secondary-darkest)'}}>
          It gets the job done.
        </p>
        <p style={{fontSize: '1.1rem', color: 'var(--ifm-color-secondary-darkest)'}}>
          {siteConfig.tagline}
        </p>
        <div style={{marginTop: '2rem', display: 'flex', gap: '1rem', justifyContent: 'center'}}>
          <Link className="button button--primary button--lg" to="/docs/getting-started">
            Get Started
          </Link>
          <Link className="button button--secondary button--lg" href="https://github.com/moberghr/warp">
            GitHub
          </Link>
        </div>
      </div>
    </header>
  );
}

function Features() {
  const features = [
    { title: 'Messages & Jobs', description: 'Two patterns in one library. Pub/sub messages with multiple handlers, and orchestrated jobs with scheduling, retries, continuations, and batches.' },
    { title: 'Built on EF Core', description: 'Uses your existing DbContext. Jobs are created in the same transaction as your business data (outbox pattern). Supports PostgreSQL and SQL Server.' },
    { title: 'Real-time Dashboard', description: 'Built-in dashboard with live graphs, job detail with full exception traces, pipeline logging, and job tracing across handlers.' },
    { title: 'Crash Recovery', description: 'Per-job keep-alive heartbeat with automatic requeue on crash. No lost jobs, no wasted retries. Sliding invisibility timeout with configurable thresholds.' },
    { title: 'Pipeline Behaviors', description: 'Middleware chain wrapping all handler invocations. Add logging, metrics, validation, or authorization across all jobs and messages.' },
    { title: 'Job Tracing', description: 'Automatic trace propagation. When a handler spawns new jobs, they share a TraceId. See the full execution flow in the dashboard.' },
  ];
  return (
    <section style={{padding: '4rem 0'}}>
      <div className="container">
        <div className="row">
          {features.map((f, i) => (
            <div key={i} className="col col--4" style={{marginBottom: '2rem'}}>
              <h3>{f.title}</h3>
              <p>{f.description}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function Screenshots() {
  const imgStyle = {width: '100%', borderRadius: '8px', boxShadow: '0 4px 20px rgba(0,0,0,0.15)'};
  return (
    <section style={{padding: '2rem 0 4rem', background: 'var(--ifm-color-emphasis-100)'}}>
      <div className="container">
        <h2 style={{textAlign: 'center', marginBottom: '2rem'}}>Dashboard</h2>
        <img src="/warp/img/screenshots/01-dashboard.png" alt="Dashboard" style={imgStyle} data-theme-target="light" />
        <img src="/warp/img/screenshots/01-dashboard-dark.png" alt="Dashboard" style={imgStyle} data-theme-target="dark" />
      </div>
    </section>
  );
}

export default function Home(): JSX.Element {
  return (
    <Layout description="Distributed job processing and message queue for .NET">
      <Hero />
      <main>
        <Features />
        <Screenshots />
      </main>
    </Layout>
  );
}
