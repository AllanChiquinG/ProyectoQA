// Auditoría automática con axe-core de las pantallas por rol. Falla ante infracciones graves o críticas y guarda el detalle.
const pantallas = {
  publico: { paginas: ['/Login'] },
  recepcion: { correo: 'elena.vargas@example.com', paginas: ['/', '/Pacientes', '/Agenda', '/Citas/Nueva'] },
  administrador: { correo: 'admin@example.com', paginas: ['/', '/Medicos', '/Usuarios'] },
  medico: { correo: 'lucia.mendez@example.com', paginas: ['/', '/MiAgenda', '/Pacientes'] },
  laboratorio: { correo: 'daniela.reyes@example.com', paginas: ['/', '/Laboratorio', '/Laboratorio/Registrar'] },
};

const resultados = [];

describe('Accesibilidad con axe-core', () => {
  Object.entries(pantallas).forEach(([rol, { correo, paginas }]) => {
    paginas.forEach((pagina) => {
      it(`${rol}: ${pagina}`, () => {
        if (correo) cy.loginUi(correo);
        cy.visit(pagina, { failOnStatusCode: false });
        cy.readFile('node_modules/axe-core/axe.min.js').then((codigo) => {
          cy.window({ log: false }).then(async (ventana) => {
            ventana.eval(codigo);
            const r = await ventana.axe.run(ventana.document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] });
            const infracciones = r.violations.map((v) => ({ id: v.id, impacto: v.impact, ayuda: v.help, nodos: v.nodes.map((n) => n.target.join(' ')).slice(0, 8), total: v.nodes.length }));
            resultados.push({ rol, pagina, ruta: ventana.location.pathname, aprobadas: r.passes.length, infracciones });
            const graves = infracciones.filter((v) => ['serious', 'critical'].includes(v.impacto));
            expect(graves.map((v) => `${v.id}: ${v.total}`), 'infracciones graves o críticas').to.deep.equal([]);
          });
        });
      });
    });
  });

  after(() => cy.writeFile('../../reports/accesibilidad/axe.json', resultados));
});
