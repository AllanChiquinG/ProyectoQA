// Fechas con la hora de la clínica (misma zona que la API: Clinic:TimeZone).
const ZONE = 'America/Guatemala';

export const MONTHS = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];

/** Hoy en la clínica, AAAA-MM-DD. */
export const clinicToday = () => new Date().toLocaleDateString('en-CA', { timeZone: ZONE });

export const addDays = (iso, days) => {
  const d = new Date(`${iso}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

/** "Noviembre 2026", como lo muestra el calendario. */
export const monthLabel = (iso) => `${MONTHS[Number(iso.slice(5, 7)) - 1]} ${iso.slice(0, 4)}`;

export const firstOfMonth = (iso, offset = 0) => {
  const [y, m] = iso.split('-').map(Number);
  const d = new Date(Date.UTC(y, m - 1 + offset, 1));
  return d.toISOString().slice(0, 10);
};
