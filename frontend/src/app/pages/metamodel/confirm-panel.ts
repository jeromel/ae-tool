import { AfterViewInit, Component, ElementRef, inject, input, output } from '@angular/core';

/**
 * Confirmation d'une action destructrice, dans la page (pas de boîte du navigateur) : le titre dit l'action,
 * le contenu projeté dit ce qui va changer, chiffres à l'appui. Le focus arrive sur « Annuler ».
 */
@Component({
  selector: 'app-confirm',
  template: `
    <div class="confirm" role="alertdialog" [attr.aria-label]="title()">
      <h3>{{ title() }}</h3>
      <div class="body"><ng-content /></div>
      <div class="tools">
        <button type="button" class="ghost" data-cancel (click)="cancel.emit()">Annuler</button>
        <button type="button" [class.danger]="danger()" [disabled]="disabled()" (click)="confirm.emit()">{{ action() }}</button>
      </div>
    </div>
  `,
})
export class ConfirmPanel implements AfterViewInit {
  title = input.required<string>();
  action = input.required<string>();
  danger = input(true);
  disabled = input(false);
  confirm = output<void>();
  cancel = output<void>();
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngAfterViewInit(): void {
    this.host.nativeElement.querySelector<HTMLElement>('[data-cancel]')?.focus();
  }
}
