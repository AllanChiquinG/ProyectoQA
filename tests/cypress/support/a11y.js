/**
 * Comprobaciones de accesibilidad sencillas, sin dependencias: todo control de formulario visible tiene nombre accesible (etiqueta que lo envuelve,
 * label for, aria-label o aria-labelledby), y la página no se desborda en horizontal. No sustituyen a una auditoría con axe/Lighthouse (pendiente).
 */
export const assertControlsLabelled = () =>
  cy.get('input:visible, select:visible, textarea:visible').then(($controls) => {
    const sinNombre = [...$controls]
      .filter((el) => !['hidden', 'submit', 'button'].includes(el.type))
      .filter((el) => {
        if (el.closest('label')) return false;
        if (el.getAttribute('aria-label') || el.getAttribute('aria-labelledby')) return false;
        if (el.id && el.ownerDocument.querySelector(`label[for="${CSS.escape(el.id)}"]`)) return false;
        return true;
      })
      .map((el) => `${el.tagName.toLowerCase()}[name=${el.name || el.id}]`);
    expect(sinNombre, 'controles sin nombre accesible').to.deep.equal([]);
  });

export const assertNoHorizontalOverflow = (que = 'la página') =>
  cy.document().then((doc) => expect(doc.documentElement.scrollWidth, `ancho de ${que}`).to.be.at.most(doc.documentElement.clientWidth + 1));
