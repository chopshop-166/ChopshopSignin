#!/usr/bin/env python

from http import HTTPStatus

from flask import Response, jsonify, request
from flask.templating import render_template

from . import app
from .model import SqliteModel

model = SqliteModel()


@app.route("/")
def index():
    return render_template("index.html")


@app.route("/scan", methods=['POST'])
def scan():
    event = request.values['event']
    name = request.values['name']
    stamp = model.scan(event, name)

    if stamp:
        return jsonify({
            'stamp': stamp.event,
            'message': f"{stamp.name} signed {stamp.event}",
            'users': model.get_active(event)
        })
    else:
        return Response("Error: Not a valid QR code", HTTPStatus.BAD_REQUEST)


@app.route("/active")
def active_all():
    return jsonify(model.get_all_active())


@app.route("/active/<event>")
def active(event):
    return jsonify(model.get_active(event))


@app.route("/stamps")
def stamps():
    return jsonify(model.get_all_stamps())
