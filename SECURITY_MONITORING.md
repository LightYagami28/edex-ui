# 24/7 Automated Vulnerability Monitoring System

This repository has been configured with comprehensive, continuous security monitoring to ensure all dependencies are always checked for vulnerabilities around the clock.

## Overview

The security monitoring system consists of multiple layers of automated checks:

### 1. Dependabot Configuration (`.github/dependabot.yml`)
- **Automated Daily Scans**: Dependabot scans both root and `src/` package dependencies daily
- **Security Updates**: Automatically creates pull requests for security vulnerabilities
- **Version Updates**: Creates grouped PRs for minor and patch updates to keep dependencies current
- **GitHub Actions Updates**: Weekly checks for updates to workflow actions

**Schedule:**
- Root dependencies: Daily at 02:00 UTC
- Source dependencies: Daily at 03:00 UTC
- GitHub Actions: Weekly on Monday at 04:00 UTC

### 2. Daily Vulnerability Scanning (`.github/workflows/vulnerability-scan.yml`)
**Runs:** Daily at 00:00 UTC, on push to master, and on pull requests

**Features:**
- **npm audit**: Scans both root and src dependencies using npm's built-in vulnerability database
- **Outdated Dependency Check**: Identifies packages that have newer versions available
- **Dependency Review**: Analyzes PRs for new vulnerable dependencies (PRs only)
- **Automatic Issue Creation**: Creates GitHub issues when critical/high severity vulnerabilities are detected
- **Audit Reports**: Stores detailed audit results as artifacts for 30 days

### 3. Advanced Security Scanning (`.github/workflows/security-scan.yml`)
**Runs:** Every 6 hours (4 times daily), on push to master, and on pull requests

**Multiple Scanning Tools:**

#### Snyk Scanner
- Industry-leading vulnerability database
- Deep dependency tree analysis
- Actionable remediation advice
- **Note**: Requires `SNYK_TOKEN` secret to be configured

#### Trivy Scanner
- Comprehensive vulnerability detection
- Fast and accurate scanning
- SARIF output integrated with GitHub Security tab
- No authentication required

#### OSV Scanner (Google)
- Open Source Vulnerabilities database
- Multi-ecosystem support
- Real-time vulnerability data
- JSON reports stored as artifacts

#### OWASP Dependency-Check
- Identifies known vulnerabilities (CVEs)
- Supports multiple package managers
- Generates detailed HTML and JSON reports
- Scans both production and development dependencies

#### License Compliance Check
- Ensures all dependencies use approved licenses
- Prevents introduction of incompatible licenses
- Checks both root and src dependencies

### 4. Enhanced CodeQL Analysis (`.github/workflows/codeql-analysis.yml`)
**Runs:** Three times per week (Monday, Wednesday, Friday), on push to master, and on pull requests

**Features:**
- Static code analysis for security vulnerabilities
- Security-extended query suite for comprehensive coverage
- Integrated with GitHub Security tab
- Detects common security patterns and anti-patterns

### 5. SonarQube Code Quality Analysis (`.github/workflows/sonarqube.yml`)
**Runs:** On push to master and on pull requests

**Features:**
- **Multi-Platform Analysis**: Runs on Ubuntu, Windows, and macOS for comprehensive coverage
- **Code Quality Metrics**: Analyzes code quality, bugs, vulnerabilities, and code smells
- **Technical Debt**: Tracks technical debt and maintainability
- **Pull Request Decoration**: Shows quality gate status directly on PRs
- **SonarCloud Integration**: Results visible on SonarCloud dashboard
- **Note**: Requires `SONAR_TOKEN` secret to be configured

**Configuration:**
- Project configuration: `sonar-project.properties`
- Project key: `LightYagami28_edex-ui`
- Organization: `lightyagami28`

## Coverage Schedule

The monitoring system provides true 24/7 coverage:

| Time (UTC) | Check Type | Workflow |
|------------|-----------|----------|
| 00:00 daily | Full vulnerability scan | vulnerability-scan.yml |
| 02:00 daily | Dependabot (root deps) | dependabot.yml |
| 03:00 daily | Dependabot (src deps) | dependabot.yml |
| 04:00 Monday | Dependabot (actions) | dependabot.yml |
| Every 6 hours | Advanced security scan | security-scan.yml |
| Mon/Wed/Fri | CodeQL analysis | codeql-analysis.yml |
| On every push | All applicable workflows (including SonarQube) | All |
| On every PR | All applicable workflows (including SonarQube) | All |

## Notifications

### Automated Notifications
1. **GitHub Issues**: Critical/high vulnerabilities trigger automatic issue creation
2. **Pull Requests**: Dependabot automatically creates PRs for security updates
3. **Security Tab**: All findings appear in the GitHub Security tab
4. **Workflow Notifications**: Failed workflows send GitHub notifications

### Manual Monitoring
- Check the "Security" tab in GitHub for all detected vulnerabilities
- Review "Actions" tab for workflow run history
- Monitor pull requests from Dependabot for updates

## Configuration Requirements

### Required Secrets (Optional but Recommended)
To enable all features, configure these secrets in repository settings:

1. **SNYK_TOKEN**: For Snyk vulnerability scanning
   - Sign up at https://snyk.io/
   - Get your token from Account Settings
   - Add as repository secret

2. **SONAR_TOKEN**: For SonarQube/SonarCloud analysis
   - Sign up at https://sonarcloud.io/
   - Get your token from https://sonarcloud.io/account/security
   - Add as repository secret

3. **SONAR_HOST_URL** (Optional): For self-hosted SonarQube
   - Default is https://sonarcloud.io (not needed for SonarCloud)
   - Only required if using a self-hosted SonarQube instance
   - Add as repository secret if needed

### Permissions
Ensure GitHub Actions has these permissions (usually default):
- `actions: read`
- `contents: read`
- `security-events: write`

## Manual Vulnerability Checks

You can manually check for vulnerabilities at any time:

```bash
# Check root dependencies
npm audit

# Check src dependencies
cd src && npm audit

# Check for outdated packages
npm outdated
cd src && npm outdated

# Fix automatically fixable vulnerabilities
npm audit fix
cd src && npm audit fix
```

## Responding to Vulnerabilities

When vulnerabilities are detected:

1. **Review the Alert**: Check the GitHub Security tab or issue created
2. **Assess Impact**: Determine if the vulnerability affects your code
3. **Update Dependencies**: 
   - Accept Dependabot PRs if available
   - Run `npm audit fix` for automatic fixes
   - Manually update if needed: `npm update <package>`
4. **Test Changes**: Ensure updates don't break functionality
5. **Deploy**: Merge the fix and deploy

## Maintenance

### Weekly Tasks
- Review Dependabot PRs and merge them
- Check the Security tab for new alerts

### Monthly Tasks
- Review outdated dependencies report
- Update major version dependencies if needed
- Review and close resolved security issues

## Disabling Specific Scans

If you need to temporarily disable a specific scan:

1. **Dependabot**: Comment out or remove sections in `.github/dependabot.yml`
2. **Workflows**: Disable workflow in Actions tab or remove the workflow file
3. **Specific Tools**: Comment out jobs in the workflow files

## Additional Resources

- [GitHub Dependabot Documentation](https://docs.github.com/en/code-security/dependabot)
- [npm audit Documentation](https://docs.npmjs.com/cli/v8/commands/npm-audit)
- [Snyk Documentation](https://docs.snyk.io/)
- [Trivy Documentation](https://aquasecurity.github.io/trivy/)
- [OWASP Dependency-Check](https://owasp.org/www-project-dependency-check/)
- [CodeQL Documentation](https://codeql.github.com/docs/)
- [SonarQube Documentation](https://docs.sonarqube.org/)
- [SonarCloud Documentation](https://docs.sonarcloud.io/)

## Security Best Practices

### GitHub Actions Security
All workflows in this repository follow GitHub Actions security best practices:

1. **Full Commit SHA Hashes**: All third-party actions use full commit SHA hashes instead of branch references or tags for:
   - Reproducible builds
   - Protection against malicious updates
   - Better security posture
   - Example: `uses: snyk/actions/node@9adf32b1121593767fc3c057af55b55db032dc04`

2. **Explicit Permissions**: Every job has explicit permissions defined (principle of least privilege)
   - `contents: read` for checkout operations
   - `security-events: write` only for SARIF uploads
   - `issues: write` only for issue creation

3. **No Shallow Clones**: Analysis workflows use `fetch-depth: 0` for better relevancy

## Support

For issues with the security monitoring system:
1. Check workflow logs in the Actions tab
2. Review this documentation
3. Create an issue in the repository
4. Contact the security team as per SECURITY.md
