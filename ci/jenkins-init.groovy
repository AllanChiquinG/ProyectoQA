// Primera configuración de Jenkins: usuario administrador, credencial de SonarQube y trabajo de la rama indicada.
import com.cloudbees.plugins.credentials.CredentialsScope
import com.cloudbees.plugins.credentials.SystemCredentialsProvider
import com.cloudbees.plugins.credentials.domains.Domain
import hudson.model.Result
import hudson.plugins.git.BranchSpec
import hudson.plugins.git.GitSCM
import hudson.security.FullControlOnceLoggedInAuthorizationStrategy
import hudson.security.HudsonPrivateSecurityRealm
import hudson.util.Secret
import jenkins.model.Jenkins
import org.jenkinsci.plugins.plaincredentials.impl.StringCredentialsImpl
import org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition
import org.jenkinsci.plugins.workflow.job.WorkflowJob

def j = Jenkins.get()
def realm = new HudsonPrivateSecurityRealm(false)
realm.createAccount('admin', System.getenv('JENKINS_ADMIN_PASSWORD'))
j.setSecurityRealm(realm)
j.setAuthorizationStrategy(new FullControlOnceLoggedInAuthorizationStrategy())

def store = SystemCredentialsProvider.getInstance()
store.getCredentials().removeAll { it.id == 'sonar-token' }
store.getCredentials().add(new StringCredentialsImpl(CredentialsScope.GLOBAL, 'sonar-token', 'Token de SonarQube', Secret.fromString(System.getenv('SONAR_TOKEN') ?: 'pendiente')))
store.save()

def repo = System.getenv('MEDICORE_REPO') ?: 'file:///repo'
def branch = System.getenv('MEDICORE_BRANCH') ?: 'Dev_Diego'
if (j.getItem('MediCore') == null) {
    def job = j.createProject(WorkflowJob, 'MediCore')
    job.setDefinition(new CpsScmFlowDefinition(new GitSCM(GitSCM.createRepoList(repo, null), [new BranchSpec("*/${branch}")], null, null, []), 'Jenkinsfile'))
    job.save()
}
j.save()
