﻿module MyAndromeda.Stores.OpeningHours {

    var app = angular.module("MyAndromeda.Store.OpeningHours.Directives", []);

    app.directive("occasionTaskEditor", () => {
        return {
            name: "occasionTaskEditor",
            scope: {
                task: "=task",
            },
            templateUrl: "occasionTaskEditor.html",
            controller: ($scope) => {
                Logger.Notify("Occasion task editor - started");
            }
        };
    });

    app.directive("occasionTask", () => {

        return {
            name: "occasionTaskController",
            scope: {
                task: "=task",
            },
            templateUrl: "occasionTask.html",
            controller: ($scope, $element) => {

                var task: Models.IOccasionTask = $scope.task;

                var state = {
                    occasions: task.Occasions.split(','),
                };

                var extra = {
                    hours: Math.abs(task.end.getTime() - task.start.getTime()) / 36e5,
                    startTime: kendo.toString(task.start, "HH:mm"),
                    endTime: kendo.toString(task.end, "HH:mm")
                };

                var definitions = Models.occasionDefinitions;
                $scope.getColour = (name: string) => {
                    switch (name)
                    {
                        case definitions.Delivery.Name: return Models.occasionDefinitions.Delivery.Colour; 
                        case definitions.Collection.Name: return Models.occasionDefinitions.Collection.Colour; 
                        case definitions.DineIn.Name: return Models.occasionDefinitions.DineIn.Colour; 
                    }
                };
                $scope.state = state;
                $scope.extra = extra; 

                let topElement = $($element).closest(".k-event");

                var popover = topElement.popover({
                    title: "Task preview",
                    placement: "auto",
                    html: true,
                    content: "please wait",
                    trigger: "click"
                }).on("show.bs.popover", function () {
                    let html = topElement.html();
                    popover.attr('data-content', html);
                    var current = $(this);
                    setTimeout(() => { current.popover('hide'); }, 5000)
                });
                
            }
            
        };

    });

} 