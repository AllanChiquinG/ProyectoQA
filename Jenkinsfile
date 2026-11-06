// Pipeline de MediCore: 18 etapas. Una prueba crítica fallida, una vulnerabilidad crítica no aceptada o un Quality Gate
// rechazado detienen el despliegue final (etapa "Quality Gate", scripts/quality-gate.py).
pipeline {
    agent any
    options {
        disableConcurrentBuilds()
        timestamps()
        timeout(time: 3, unit: 'HOURS')
        buildDiscarder(logRotator(numToKeepStr: '15'))
    }
    parameters {
        booleanParam(name: 'SIMULAR_FALLO', defaultValue: false, description: 'Defensa: agrega una comprobación que falla para demostrar que el despliegue se detiene.')
        string(name: 'SONAR_URL', defaultValue: 'http://sonarqube:9000', description: 'Servidor de SonarQube')
    }
    environment {
        COMPOSE_INTERACTIVE_NO_CLI = '1'
        NVD_VOLUME = 'medicore-dependency-check'
    }
    stages {
        stage('1. Checkout') {
            steps { checkout scm; sh 'git log -1 --oneline' }
        }
        stage('2. Dependencias') {
            steps {
                sh 'bash scripts/dotnet-linux.sh build MediCore.slnx'
                sh 'docker run --rm -v "$PWD/tests/cypress:/app" -w /app node:22-alpine npm ci --ignore-scripts'
            }
        }
        stage('3. Lint y validaciones') {
            steps {
                sh 'bash scripts/dotnet-linux.sh format'
                sh 'bash scripts/validate-iac.sh'
            }
        }
        stage('4. Pruebas unitarias e integración') {
            steps {
                sh 'bash scripts/dotnet-linux.sh test MediCore.slnx'
            }
        }
        stage('5. SonarQube') {
            steps {
                withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
                    sh 'SONAR_HOST_URL="$SONAR_URL" bash scripts/sec-sonar.sh'
                }
            }
        }
        stage('6. npm audit') {
            steps { sh 'bash scripts/sec-npm-audit.sh' }
        }
        stage('7. OWASP Dependency-Check') {
            steps { sh 'bash scripts/sec-dependency-check.sh' }
        }
        stage('8. Fortify') {
            steps {
                script {
                    if (env.FORTIFY_SCA_PATH?.trim()) {
                        sh 'bash scripts/sec-fortify.sh'
                    } else {
                        unstable('Fortify sin licencia en este agente: la etapa queda registrada como pendiente.')
                    }
                }
            }
        }
        stage('9. Docker Build') {
            steps {
                sh 'test -f .env.qa || cp /repo/.env.qa .env.qa 2>/dev/null || bash scripts/qa.sh init'
                sh 'docker compose -p medicore-qa --env-file .env.qa -f docker-compose.yml -f docker-compose.qa.yml build'
            }
        }
        stage('10. Deploy a QA') {
            steps {
                sh 'bash scripts/qa.sh up'
                sh 'bash scripts/qa.sh seed'
                sh 'bash scripts/qa.sh smoke'
            }
        }
        stage('11. Pruebas API') {
            steps { sh 'bash scripts/api-tests.sh' }
        }
        stage('12. Cypress') {
            steps { sh 'bash scripts/cypress-tests.sh' }
        }
        stage('13. Maestro') {
            steps {
                script {
                    if (env.ANDROID_SERIAL?.trim()) {
                        sh 'python3 scripts/maestro_run.py'
                    } else {
                        unstable('Sin emulador Android en este agente: Maestro se ejecuta en el equipo con Android SDK.')
                    }
                }
            }
        }
        stage('14. OWASP ZAP') {
            steps { sh 'bash scripts/sec-zap.sh' }
        }
        stage('15. Rendimiento') {
            steps { sh 'bash scripts/perf-tests.sh load' }
        }
        stage('16. Quality Gate') {
            steps {
                script {
                    if (params.SIMULAR_FALLO) { error('Fallo simulado: el despliegue final queda bloqueado.') }
                }
                sh 'python3 scripts/quality-gate.py'
            }
        }
        stage('17. Aprobación') {
            steps {
                input message: '¿Aprobar el despliegue final?', ok: 'Aprobar'
            }
        }
        stage('18. Deploy final') {
            steps {
                script {
                    if (env.AZURE_QA_HOST?.trim()) {
                        sh 'ansible-playbook -i infrastructure/ansible/inventory/qa.ini infrastructure/ansible/site.yml'
                    } else {
                        echo 'Sin servidor de Azure definido: el despliegue final se limita al ambiente QA local.'
                    }
                }
            }
        }
    }
    post {
        always {
            archiveArtifacts artifacts: 'reports/**/*', allowEmptyArchive: true
        }
    }
}
