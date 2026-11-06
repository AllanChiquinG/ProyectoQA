# Jenkins con Docker CLI, Compose y Python. Usa el Docker del anfitrión por el socket.
FROM jenkins/jenkins:lts-jdk21
USER root
RUN apt-get update && apt-get install -y --no-install-recommends docker.io docker-compose python3 curl \
    && mkdir -p /usr/local/lib/docker/cli-plugins \
    && curl -fsSL https://github.com/docker/compose/releases/download/v2.39.1/docker-compose-linux-x86_64 -o /usr/local/lib/docker/cli-plugins/docker-compose \
    && chmod +x /usr/local/lib/docker/cli-plugins/docker-compose \
    && rm -rf /var/lib/apt/lists/*
RUN jenkins-plugin-cli --plugins workflow-aggregator git pipeline-stage-view credentials-binding timestamper configuration-as-code
COPY ci/jenkins-init.groovy /usr/share/jenkins/ref/init.groovy.d/medicore.groovy
ENV JAVA_OPTS="-Djenkins.install.runSetupWizard=false -Dhudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true"
