import { Component, input } from '@angular/core';

/**
 * Mention de périmètre (maquette « périmètre de lecture métier », §3) : la même partout, pour être reconnue à la deuxième
 * rencontre. Elle n'apparaît que là où la réponse a perdu des éléments, et ne donne jamais de nombre. Le mot « métier »
 * ne s'affiche pas : la mention décrit ce que la personne voit.
 */
@Component({
  selector: 'app-scope-note',
  template: `
    <div class="note small scope-note" role="note">
      {{ text() }}
      <details>
        <summary>Pourquoi ?</summary>
        L'infrastructure (serveurs, réseaux, équipements industriels) n'est visible que des équipes informatiques. Pour un accès
        complet, adressez-vous à l'administrateur de l'outil.
      </details>
    </div>
  `,
  styles: `
    .scope-note { margin: 8px 0 12px; }
    .scope-note details { display: inline; margin-left: 6px; }
    .scope-note summary { display: inline; cursor: pointer; text-decoration: underline; }
    .scope-note details[open] { display: block; margin: 6px 0 0; }
  `,
})
export class ScopeNote {
  text = input('Infrastructure non affichée : votre accès couvre les processus, applications, flux et données.');
}
