# Security Status Dashboard

## Current Security Monitoring Status

### Automated Workflows

| Workflow | Status | Frequency | Last Run |
|----------|--------|-----------|----------|
| Daily Vulnerability Scan | ![Vulnerability Scan](../../workflows/Daily%20Vulnerability%20Scan/badge.svg) | Daily at 00:00 UTC | Check Actions tab |
| Advanced Security Scanning | ![Security Scan](../../workflows/Advanced%20Security%20Scanning/badge.svg) | Every 6 hours | Check Actions tab |
| CodeQL Analysis | ![CodeQL](../../workflows/CodeQL/badge.svg) | Mon/Wed/Fri | Check Actions tab |
| Dependabot | ✅ Active | Daily | Check PRs tab |

### Security Tools Active

- ✅ **Dependabot** - Automated dependency updates
- ✅ **npm audit** - Built-in vulnerability scanning
- ✅ **Snyk** - Advanced vulnerability detection (requires token)
- ✅ **Trivy** - Container and filesystem scanning
- ✅ **OSV Scanner** - Google's vulnerability database
- ✅ **OWASP Dependency-Check** - CVE detection
- ✅ **CodeQL** - Static code analysis
- ✅ **Dependency Review** - PR-based analysis
- ✅ **License Checker** - Compliance verification

### Quick Actions

- 🔍 [View Security Advisories](../../security/advisories)
- 🛡️ [View Dependabot Alerts](../../security/dependabot)
- 📊 [View Code Scanning Alerts](../../security/code-scanning)
- 🔧 [View Workflow Runs](../../actions)
- 📝 [View Open Security Issues](../../issues?q=is%3Aissue+is%3Aopen+label%3Asecurity)

### Dependency Status

Check current dependency security status:

```bash
# Root dependencies
npm audit

# Source dependencies  
cd src && npm audit
```

### Coverage Map

```
Time (UTC)  | Monitoring Activity
------------|--------------------
00:00       | ██ Full Vulnerability Scan
02:00       | ██ Dependabot Root Scan
03:00       | ██ Dependabot Src Scan
06:00       | ██ Advanced Security Scan
12:00       | ██ Advanced Security Scan
18:00       | ██ Advanced Security Scan
Every Push  | ██ All Applicable Scans
Every PR    | ██ PR Security Checks
```

### Security Metrics

For detailed security metrics and vulnerability trends, check:
- GitHub Security tab → Insights
- Actions tab → Workflow runs
- Security tab → Dependabot alerts

---

**Last Updated**: Automatically maintained by GitHub Actions  
**Documentation**: See [SECURITY_MONITORING.md](../../SECURITY_MONITORING.md)  
**Report Issues**: See [SECURITY.md](../../SECURITY.md)
