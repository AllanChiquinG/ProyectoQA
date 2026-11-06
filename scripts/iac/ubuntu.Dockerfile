# Servidor desechable para ejecutar Ansible sin Azure: Ubuntu 24.04 (la misma imagen de la máquina virtual) con systemd, SSH y Python, como una VM recién creada.
# Corre como root a propósito: systemd lo necesita. Solo existe durante scripts/iac-simulacion.sh y no se publica.
FROM ubuntu:24.04
ENV container=docker
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        dbus \
        git \
        iptables \
        kmod \
        openssh-server \
        python3 \
        python3-venv \
        sudo \
        systemd \
        systemd-sysv \
    && systemctl mask getty.target console-getty.service systemd-udevd.service systemd-firstboot.service \
    && rm -rf /var/lib/apt/lists/*
STOPSIGNAL SIGRTMIN+3
CMD ["/sbin/init"]
